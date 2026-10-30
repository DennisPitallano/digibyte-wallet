using System.Text;
using System.Text.Json;
using DigiByte.Api.Services;
using DigiByte.P2P.Shared.Sponsors;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DigiByte.Api.Tests;

public class SponsorSubmissionServiceTests
{
    private static readonly string Tx = new('a', 64);
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private static readonly byte[] WebP = [(byte)'R', (byte)'I', (byte)'F', (byte)'F', 1, 2, 3, 4, (byte)'W', (byte)'E', (byte)'B', (byte)'P', 9];

    private static (SponsorSubmissionService svc, FakeGitHub gh, FakeVerifier verifier) Create(FakeGitHub? gh = null, FakeVerifier? verifier = null, int cap = 50)
    {
        gh ??= new FakeGitHub();
        verifier ??= new FakeVerifier();
        var options = Options.Create(new SponsorOptions { MaxOpenSubmissions = cap });
        return (new SponsorSubmissionService(gh, verifier, options, NullLogger<SponsorSubmissionService>.Instance), gh, verifier);
    }

    private static SponsorSubmissionRequest Add(string name = "Alice", string? link = "https://alice.example", byte[]? image = null,
        string? txid = null, bool consent = true, string? website = null) =>
        new() { Type = SponsorSubmissionType.Add, Name = name, Link = link, TxId = txid ?? Tx, Consent = consent, Image = image, Website = website };

    // ── validation ──

    [Fact]
    public async Task MissingConsent_IsInvalid_AndNothingIsSent()
    {
        var (svc, gh, _) = Create();
        var r = await svc.SubmitAsync(Add(consent: false), default);
        Assert.Equal(SponsorSubmissionState.Invalid, r.State);
        Assert.Contains("consent", r.Fields!);
        Assert.Empty(gh.Calls);
    }

    [Fact]
    public async Task InvalidFields_AreNamedWithoutEchoingContent()
    {
        var (svc, gh, _) = Create();
        var r = await svc.SubmitAsync(Add(name: "", link: "http://insecure.example", txid: "xyz"), default);
        Assert.Equal(SponsorSubmissionState.Invalid, r.State);
        Assert.Equal(["link", "name", "txid"], r.Fields!.OrderBy(f => f, StringComparer.Ordinal));
        Assert.Empty(gh.Calls);
        Assert.DoesNotContain("insecure", JsonSerializer.Serialize(r, SponsorJson.Options));
    }

    [Fact]
    public async Task NonImageBytes_AndOversizedImages_AreInvalid()
    {
        var (svc, _, _) = Create();
        var notImage = await svc.SubmitAsync(Add(image: "<svg/>"u8.ToArray()), default);
        Assert.Contains("image", notImage.Fields!);

        var big = new byte[SponsorRules.ImageMaxBytes + 1];
        Png.CopyTo(big, 0);
        var tooBig = await svc.SubmitAsync(Add(image: big), default);
        Assert.Contains("image", tooBig.Fields!);
    }

    [Fact]
    public async Task Honeypot_LooksLikeSuccess_ButDoesNothing()
    {
        var (svc, gh, verifier) = Create();
        var r = await svc.SubmitAsync(Add(website: "http://spam.example"), default);
        Assert.Equal(SponsorSubmissionState.Created, r.State);
        Assert.Empty(gh.Calls);
        Assert.Equal(0, verifier.Calls);
    }

    // ── happy path ──

    [Fact]
    public async Task ValidAdd_CreatesBranchFromTxid_CommitsEntryClaimAndImage_AndOpensLabelledPullRequest()
    {
        var (svc, gh, _) = Create();
        var r = await svc.SubmitAsync(Add(image: WebP), default);
        Assert.Equal(SponsorSubmissionState.Created, r.State);

        var id = SponsorRules.ComputeEntryId("Alice", "https://alice.example", WebP);
        Assert.Contains($"CreateBranch submission/{new string('a', 16)}", gh.Calls);
        Assert.Contains($"Put data/entries/{id}.json", gh.Calls);
        Assert.Contains($"Put data/donations/{Tx}.txt", gh.Calls);
        Assert.Contains($"Put data/images/{id}.webp", gh.Calls);
        Assert.Equal(["sponsor-submission", "donation-verified"], gh.PrLabels);

        var entry = JsonDocument.Parse(gh.Files[$"data/entries/{id}.json"]).RootElement;
        Assert.Equal(id, entry.GetProperty("id").GetString());
        Assert.Equal("Alice", entry.GetProperty("name").GetString());
        Assert.Equal($"{id}.webp", entry.GetProperty("image").GetString());
        Assert.Equal("https://alice.example", entry.GetProperty("link").GetString());
        Assert.Equal(["id", "image", "link", "name"], entry.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(id + "\n", Encoding.UTF8.GetString(Convert.FromHexString(Convert.ToHexString(gh.FileBytes[$"data/donations/{Tx}.txt"]))));
    }

    [Fact]
    public async Task EntryWithoutImageOrLink_HasOnlyIdAndName()
    {
        var (svc, gh, _) = Create();
        await svc.SubmitAsync(Add(link: null), default);
        var id = SponsorRules.ComputeEntryId("Alice", null, []);
        var entry = JsonDocument.Parse(gh.Files[$"data/entries/{id}.json"]).RootElement;
        Assert.Equal(["id", "name"], entry.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.DoesNotContain(gh.Calls, c => c.StartsWith("Put data/images/"));
    }

    [Fact]
    public async Task PngUpload_IsStoredWithItsDetectedExtension()
    {
        var (svc, gh, _) = Create();
        await svc.SubmitAsync(Add(image: Png), default);
        var id = SponsorRules.ComputeEntryId("Alice", "https://alice.example", Png);
        Assert.Contains($"Put data/images/{id}.png", gh.Calls);
    }

    [Fact]
    public async Task PullRequest_RecordsTxidAndConsent_AndNotTheTokenOrAnythingPrivate()
    {
        var (svc, gh, _) = Create();
        await svc.SubmitAsync(Add(), default);
        Assert.Contains(Tx, gh.PrBody);
        Assert.Contains("permission", gh.PrBody, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Sponsor submission: Alice", gh.PrTitle);
    }

    [Fact]
    public async Task PullRequestTitle_IsSanitised()
    {
        var (svc, gh, _) = Create();
        await svc.SubmitAsync(Add(name: "  Alice     Ltd  "), default);
        Assert.Equal("Sponsor submission: Alice Ltd", gh.PrTitle);
    }

    // ── one donation, one entry ──

    [Fact]
    public async Task UsedTransaction_IsRefused_WithoutVerifyingOrBranching()
    {
        var gh = new FakeGitHub { Claim = "0123456789ab" };
        var (svc, _, verifier) = Create(gh);
        var r = await svc.SubmitAsync(Add(), default);
        Assert.Equal(SponsorSubmissionState.AlreadyUsed, r.State);
        Assert.Equal(0, verifier.Calls);
        Assert.DoesNotContain(gh.Calls, c => c.StartsWith("CreateBranch"));
    }

    [Theory]
    [InlineData("Alice")]
    [InlineData("  alice ")]
    [InlineData("ALICE")]
    [InlineData("Al   ice")]
    public async Task NameAlreadyListed_IsRefused_IgnoringCaseAndSpaces_WithoutVerifyingOrBranching(string typed)
    {
        var gh = new FakeGitHub();
        gh.Files["data/sponsors.json"] = """{"version":1,"sponsors":[{"id":"x","name":"Al ice"},{"id":"y","name":"Alice"}]}""";
        var (svc, _, verifier) = Create(gh);
        var r = await svc.SubmitAsync(Add(name: typed), default);
        Assert.Equal(SponsorSubmissionState.NameTaken, r.State);
        Assert.Equal(["name"], r.Fields);
        Assert.Equal(0, verifier.Calls);
        Assert.DoesNotContain(gh.Calls, c => c.StartsWith("CreateBranch"));
    }

    [Fact]
    public async Task DifferentName_OrNoListYet_IsNotBlocked()
    {
        var gh = new FakeGitHub();
        gh.Files["data/sponsors.json"] = """{"version":1,"sponsors":[{"id":"x","name":"Bob"}]}""";
        Assert.Equal(SponsorSubmissionState.Created, (await Create(gh).svc.SubmitAsync(Add(), default)).State);
        Assert.Equal(SponsorSubmissionState.Created, (await Create(new FakeGitHub()).svc.SubmitAsync(Add(), default)).State);
    }

    [Fact]
    public async Task BranchAlreadyExists_MeansAnotherSubmissionIsOpen()
    {
        var gh = new FakeGitHub { BranchExists = true };
        var (svc, _, _) = Create(gh);
        Assert.Equal(SponsorSubmissionState.AlreadyUsed, (await svc.SubmitAsync(Add(), default)).State);
        Assert.DoesNotContain(gh.Calls, c => c.StartsWith("Put "));
    }

    [Fact]
    public async Task OpenSubmissionCap_ReturnsQueueFull_BeforeAnythingIsCreated()
    {
        var gh = new FakeGitHub { OpenCount = 50 };
        var (svc, _, verifier) = Create(gh, cap: 50);
        Assert.Equal(SponsorSubmissionState.QueueFull, (await svc.SubmitAsync(Add(), default)).State);
        Assert.Equal(0, verifier.Calls);
        Assert.DoesNotContain(gh.Calls, c => c.StartsWith("CreateBranch"));
    }

    // ── donation check ──

    [Theory]
    [InlineData(DonationRejection.NotFound)]
    [InlineData(DonationRejection.TooFewConfirmations)]
    [InlineData(DonationRejection.WrongRecipient)]
    [InlineData(DonationRejection.BelowMinimum)]
    public async Task FailedDonationCheck_ReturnsTheReason_AndOpensNothing(DonationRejection reason)
    {
        var (svc, gh, _) = Create(verifier: new FakeVerifier { Result = DonationCheck.Rejected(reason) });
        var r = await svc.SubmitAsync(Add(), default);
        Assert.Equal(SponsorSubmissionState.NotVerified, r.State);
        Assert.Equal(reason, r.Reason);
        Assert.DoesNotContain(gh.Calls, c => c.StartsWith("CreateBranch"));
    }

    [Fact]
    public async Task ExplorersUnreachable_IsUnavailable_NotAPass()
    {
        var (svc, gh, _) = Create(verifier: new FakeVerifier { Throw = new DonationLookupUnavailableException("down") });
        Assert.Equal(SponsorSubmissionState.Unavailable, (await svc.SubmitAsync(Add(), default)).State);
        Assert.DoesNotContain(gh.Calls, c => c.StartsWith("CreateBranch"));
    }

    // ── failures ──

    [Fact]
    public async Task GitHubFailure_AfterTheBranchExists_DeletesTheBranch_AndIsUnavailable()
    {
        var gh = new FakeGitHub { FailOnPut = true };
        var (svc, _, _) = Create(gh);
        var r = await svc.SubmitAsync(Add(), default);
        Assert.Equal(SponsorSubmissionState.Unavailable, r.State);
        Assert.Contains($"DeleteBranch submission/{new string('a', 16)}", gh.Calls);
    }

    [Fact]
    public async Task GitHubFailure_BeforeAnyBranch_IsUnavailable_AndNothingToClean()
    {
        var gh = new FakeGitHub { FailOnHead = true };
        var (svc, _, _) = Create(gh);
        Assert.Equal(SponsorSubmissionState.Unavailable, (await svc.SubmitAsync(Add(), default)).State);
        Assert.DoesNotContain(gh.Calls, c => c.StartsWith("DeleteBranch"));
    }

    // ── removal ──

    [Fact]
    public async Task Remove_WithTheOriginalTxid_DeletesEntryAndImage_AndLabelsTheRequest()
    {
        var gh = new FakeGitHub { Claim = "0123456789ab" };
        gh.Files["data/entries/0123456789ab.json"] = """{"id":"0123456789ab","name":"Alice","image":"0123456789ab.webp"}""";
        var (svc, _, verifier) = Create(gh);
        var r = await svc.SubmitAsync(new SponsorSubmissionRequest { Type = SponsorSubmissionType.Remove, TxId = Tx }, default);
        Assert.Equal(SponsorSubmissionState.Created, r.State);
        Assert.Contains("Delete data/entries/0123456789ab.json", gh.Calls);
        Assert.Contains("Delete data/images/0123456789ab.webp", gh.Calls);
        Assert.DoesNotContain(gh.Calls, c => c.Contains("data/donations"));
        Assert.Contains("removal-request", gh.PrLabels);
        Assert.Equal(0, verifier.Calls); // the claim proves ownership; no new donation check
    }

    [Fact]
    public async Task Remove_WithATxidThatNeverCreatedAnEntry_OpensNothing()
    {
        var (svc, gh, _) = Create();
        var r = await svc.SubmitAsync(new SponsorSubmissionRequest { Type = SponsorSubmissionType.Remove, TxId = Tx }, default);
        Assert.Equal(SponsorSubmissionState.Invalid, r.State);
        Assert.Contains("txid", r.Fields!);
        Assert.DoesNotContain(gh.Calls, c => c.StartsWith("CreateBranch"));
    }

    [Fact]
    public async Task Remove_WhenTheEntryIsAlreadyGone_OpensNothing()
    {
        // The claim stays after a removal, so a repeat request finds the claim but no entry.
        var gh = new FakeGitHub { Claim = "0123456789ab" };
        var (svc, _, _) = Create(gh);
        var r = await svc.SubmitAsync(new SponsorSubmissionRequest { Type = SponsorSubmissionType.Remove, TxId = Tx }, default);
        Assert.Equal(SponsorSubmissionState.Invalid, r.State);
        Assert.Contains("txid", r.Fields!);
        Assert.DoesNotContain(gh.Calls, c => c.StartsWith("CreateBranch"));
    }

    // ── doubles ──

    private sealed class FakeVerifier : IDonationVerifier
    {
        public DonationCheck Result { get; set; } = DonationCheck.Passed;
        public Exception? Throw { get; set; }
        public int Calls { get; private set; }
        public Task<DonationCheck> VerifyAsync(string txId, CancellationToken ct)
        {
            Calls++;
            return Throw is not null ? throw Throw : Task.FromResult(Result);
        }
    }

    private sealed class FakeGitHub : IGitHubSponsorClient
    {
        public List<string> Calls { get; } = [];
        public Dictionary<string, string> Files { get; } = new();
        public Dictionary<string, byte[]> FileBytes { get; } = new();
        public string? Claim { get; set; }
        public int OpenCount { get; set; }
        public bool BranchExists { get; set; }
        public bool FailOnPut { get; set; }
        public bool FailOnHead { get; set; }
        public string[] PrLabels { get; private set; } = [];
        public string PrBody { get; private set; } = "";
        public string PrTitle { get; private set; } = "";

        public Task<string> GetBranchHeadAsync(CancellationToken ct)
        {
            Calls.Add("Head");
            return FailOnHead ? throw new GitHubApiException("GitHub answered 500") : Task.FromResult("headsha");
        }
        public Task<string?> ReadClaimAsync(string txId, CancellationToken ct) => Task.FromResult(Claim);
        public Task<string?> ReadFileTextAsync(string path, CancellationToken ct) =>
            Task.FromResult(Files.TryGetValue(path, out var v) ? v : null);
        public Task<int> CountOpenSubmissionsAsync(CancellationToken ct) => Task.FromResult(OpenCount);
        public Task<bool> CreateBranchAsync(string branch, string baseSha, CancellationToken ct)
        {
            Calls.Add($"CreateBranch {branch}");
            return Task.FromResult(!BranchExists);
        }
        public Task PutFileAsync(string branch, string path, byte[] content, string message, CancellationToken ct)
        {
            if (FailOnPut) throw new GitHubApiException("GitHub answered 500");
            Calls.Add($"Put {path}");
            FileBytes[path] = content;
            Files[path] = Encoding.UTF8.GetString(content);
            return Task.CompletedTask;
        }
        public Task DeleteFileAsync(string branch, string path, string message, CancellationToken ct)
        {
            Calls.Add($"Delete {path}");
            return Task.CompletedTask;
        }
        public Task<int> CreatePullRequestAsync(string head, string title, string body, string[] labels, CancellationToken ct)
        {
            Calls.Add($"Pr {head}");
            PrTitle = title; PrBody = body; PrLabels = labels;
            return Task.FromResult(7);
        }
        public Task DeleteBranchAsync(string branch, CancellationToken ct)
        {
            Calls.Add($"DeleteBranch {branch}");
            return Task.CompletedTask;
        }
    }
}

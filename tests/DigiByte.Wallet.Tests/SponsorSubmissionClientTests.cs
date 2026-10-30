using System.Net;
using System.Text;
using DigiByte.P2P.Shared.Sponsors;
using DigiByte.Web.Services;

namespace DigiByte.Wallet.Tests;

public class SponsorSubmissionClientTests
{
    private const string SubmitUrl = "https://api.example.test/api/sponsors/submissions";
    private static readonly string Tx = new('a', 64);

    private static (SponsorSubmissionClient client, FakeHandler http) Create(HttpStatusCode status = HttpStatusCode.Created, string body = """{"state":"created"}""")
    {
        var http = new FakeHandler { Status = status, Body = body };
        return (new SponsorSubmissionClient(new HttpClient(http), SubmitUrl), http);
    }

    private static SponsorSubmission Add() => new(SponsorSubmissionType.Add, "Alice", "https://alice.example", Tx, true, null, null);

    [Theory]
    [InlineData(HttpStatusCode.Created, """{"state":"created"}""", SponsorSubmissionState.Created)]
    [InlineData(HttpStatusCode.OK, """{"state":"duplicate"}""", SponsorSubmissionState.Duplicate)]
    [InlineData(HttpStatusCode.BadRequest, """{"state":"invalid","fields":["name"]}""", SponsorSubmissionState.Invalid)]
    [InlineData(HttpStatusCode.Conflict, """{"state":"already_used"}""", SponsorSubmissionState.AlreadyUsed)]
    [InlineData((HttpStatusCode)422, """{"state":"not_verified","reason":"too_few_confirmations"}""", SponsorSubmissionState.NotVerified)]
    [InlineData(HttpStatusCode.TooManyRequests, """{"state":"rate_limited"}""", SponsorSubmissionState.RateLimited)]
    [InlineData(HttpStatusCode.ServiceUnavailable, """{"state":"queue_full"}""", SponsorSubmissionState.QueueFull)]
    [InlineData(HttpStatusCode.BadGateway, """{"state":"unavailable"}""", SponsorSubmissionState.Unavailable)]
    public async Task EachServerAnswer_MapsToTheRightState(HttpStatusCode status, string body, SponsorSubmissionState expected)
    {
        var (client, _) = Create(status, body);
        Assert.Equal(expected, (await client.SubmitAsync(Add())).State);
    }

    [Fact]
    public async Task NotVerified_CarriesTheReason()
    {
        var (client, _) = Create((HttpStatusCode)422, """{"state":"not_verified","reason":"below_minimum"}""");
        var r = await client.SubmitAsync(Add());
        Assert.Equal(DonationRejection.BelowMinimum, r.Reason);
    }

    [Fact]
    public async Task Invalid_CarriesTheFieldNames()
    {
        var (client, _) = Create(HttpStatusCode.BadRequest, """{"state":"invalid","fields":["name","txid"]}""");
        var r = await client.SubmitAsync(Add());
        Assert.Equal(["name", "txid"], r.Fields!);
    }

    [Fact]
    public async Task NetworkError_IsUnavailable()
    {
        var http = new FakeHandler { Throw = new HttpRequestException("offline") };
        var client = new SponsorSubmissionClient(new HttpClient(http), SubmitUrl);
        Assert.Equal(SponsorSubmissionState.Unavailable, (await client.SubmitAsync(Add())).State);
    }

    [Fact]
    public async Task UnreadableAnswer_IsUnavailable_ExceptRateLimitStatus()
    {
        var (c1, _) = Create(HttpStatusCode.OK, "<html>gateway</html>");
        Assert.Equal(SponsorSubmissionState.Unavailable, (await c1.SubmitAsync(Add())).State);
        var (c2, _) = Create(HttpStatusCode.TooManyRequests, "");
        Assert.Equal(SponsorSubmissionState.RateLimited, (await c2.SubmitAsync(Add())).State);
    }

    [Fact]
    public async Task Request_GoesOnlyToTheConfiguredUrl_AndCarriesOnlyTheFormFields()
    {
        var (client, http) = Create();
        await client.SubmitAsync(new SponsorSubmission(SponsorSubmissionType.Add, "Alice", "https://alice.example", Tx, true, [1, 2, 3], "image/png"));
        Assert.Equal(SubmitUrl, http.Uri!.ToString());
        Assert.Equal(HttpMethod.Post, http.Method);
        Assert.StartsWith("multipart/form-data", http.ContentType);
        foreach (var name in new[] { "type", "name", "link", "txid", "consent", "website", "image" })
            Assert.True(HasField(http.Content, name), $"missing field {name}");
        // Nothing but the form: no wallet data, no cookies, no credentials.
        Assert.Null(http.Authorization);
        Assert.DoesNotContain("seed", http.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Fields_AreSentInTheShapeTheServerExpects()
    {
        var (client, http) = Create();
        await client.SubmitAsync(Add());
        Assert.Contains("\r\n\r\nadd\r\n", http.Content);
        Assert.Contains("\r\n\r\ntrue\r\n", http.Content);
        Assert.Contains(Tx, http.Content);
    }

    [Fact]
    public async Task Remove_SendsTypeAndTxid_WithoutNameLinkOrPicture()
    {
        var (client, http) = Create();
        await client.SubmitAsync(new SponsorSubmission(SponsorSubmissionType.Remove, null, null, Tx, false, null, null));
        Assert.Contains("\r\n\r\nremove\r\n", http.Content);
        Assert.False(HasField(http.Content, "image"));
        Assert.False(HasField(http.Content, "link"));
        Assert.False(HasField(http.Content, "name"));
    }

    // .NET writes simple field names unquoted (name=type) and others quoted (name="image"); accept both.
    private static bool HasField(string content, string name) =>
        content.Contains($"name={name}") || content.Contains($"name=\"{name}\"");

    private sealed class FakeHandler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string Body { get; set; } = "";
        public Exception? Throw { get; set; }
        public Uri? Uri { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? ContentType { get; private set; }
        public string Content { get; private set; } = "";
        public string? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Uri = request.RequestUri;
            Method = request.Method;
            ContentType = request.Content?.Headers.ContentType?.ToString();
            Content = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Authorization = request.Headers.Authorization?.ToString();
            if (Throw is not null) throw Throw;
            return new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
        }
    }
}

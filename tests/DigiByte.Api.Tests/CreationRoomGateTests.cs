using DigiByte.Api.Hubs;
using DigiByte.Crypto.Multisig;
using Microsoft.Extensions.Time.Testing;
using NBitcoin;

namespace DigiByte.Api.Tests;

public class CreationRoomGateTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero));
    private readonly CreationRoomGate _gate;
    private readonly Key _key = new();

    public CreationRoomGateTests() => _gate = new CreationRoomGate(_clock);

    private string Pub => _key.PubKey.ToHex();

    private string Proof(string nonce, string conn, string purpose = CreationRoomGate.PurposeJoin, string target = "X7K9M2", Key? key = null) =>
        CoSignerProof.Sign(key ?? _key, CoSignerProof.CreationRoomDomain, purpose, target, nonce, conn);

    [Fact]
    public void Challenge_is_a_fresh_32_byte_hex_nonce()
    {
        var a = _gate.IssueChallenge("c1");
        var b = _gate.IssueChallenge("c2");
        Assert.Equal(64, a.Length);
        Assert.True(a.All(Uri.IsHexDigit));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Valid_proof_is_accepted_once()
    {
        var nonce = _gate.IssueChallenge("c1");
        var proof = Proof(nonce, "c1");
        Assert.True(_gate.TryConsumeProof("c1", CreationRoomGate.PurposeJoin, "X7K9M2", Pub, proof));
        Assert.False(_gate.TryConsumeProof("c1", CreationRoomGate.PurposeJoin, "X7K9M2", Pub, proof)); // replay
    }

    [Fact]
    public void Failed_attempt_still_uses_up_the_nonce()
    {
        var nonce = _gate.IssueChallenge("c1");
        Assert.False(_gate.TryConsumeProof("c1", CreationRoomGate.PurposeJoin, "X7K9M2", Pub, "garbage"));
        Assert.False(_gate.TryConsumeProof("c1", CreationRoomGate.PurposeJoin, "X7K9M2", Pub, Proof(nonce, "c1")));
    }

    [Fact]
    public void Proof_is_bound_to_the_connection_it_was_issued_to()
    {
        var nonce = _gate.IssueChallenge("c1");
        _gate.IssueChallenge("c2");
        // Presented on c2 with c1's nonce: c2's own nonce differs, so it fails.
        Assert.False(_gate.TryConsumeProof("c2", CreationRoomGate.PurposeJoin, "X7K9M2", Pub, Proof(nonce, "c1")));
        // Signed for another connection id but presented on c1: fails too.
        Assert.False(_gate.TryConsumeProof("c1", CreationRoomGate.PurposeJoin, "X7K9M2", Pub, Proof(nonce, "c2")));
    }

    [Fact]
    public void Proof_without_a_challenge_fails()
    {
        Assert.False(_gate.TryConsumeProof("c9", CreationRoomGate.PurposeJoin, "X7K9M2", Pub, Proof(new string('0', 64), "c9")));
    }

    [Fact]
    public void Challenge_expires_after_60_seconds()
    {
        var nonce = _gate.IssueChallenge("c1");
        _clock.Advance(TimeSpan.FromSeconds(61));
        Assert.False(_gate.TryConsumeProof("c1", CreationRoomGate.PurposeJoin, "X7K9M2", Pub, Proof(nonce, "c1")));
    }

    [Fact]
    public void Challenge_is_valid_just_inside_60_seconds()
    {
        var nonce = _gate.IssueChallenge("c1");
        _clock.Advance(TimeSpan.FromSeconds(59));
        Assert.True(_gate.TryConsumeProof("c1", CreationRoomGate.PurposeJoin, "X7K9M2", Pub, Proof(nonce, "c1")));
    }

    [Fact]
    public void New_challenge_replaces_the_outstanding_one()
    {
        var first = _gate.IssueChallenge("c1");
        _gate.IssueChallenge("c1");
        Assert.False(_gate.TryConsumeProof("c1", CreationRoomGate.PurposeJoin, "X7K9M2", Pub, Proof(first, "c1")));
    }

    [Fact]
    public void Proof_for_another_purpose_or_target_fails()
    {
        var nonce = _gate.IssueChallenge("c1");
        Assert.False(_gate.TryConsumeProof("c1", CreationRoomGate.PurposeCreate, "X7K9M2", Pub, Proof(nonce, "c1")));
        nonce = _gate.IssueChallenge("c1");
        Assert.False(_gate.TryConsumeProof("c1", CreationRoomGate.PurposeJoin, "AAAAAA", Pub, Proof(nonce, "c1")));
    }

    [Fact]
    public void Proof_by_a_different_key_than_claimed_fails()
    {
        var nonce = _gate.IssueChallenge("c1");
        Assert.False(_gate.TryConsumeProof("c1", CreationRoomGate.PurposeJoin, "X7K9M2", Pub, Proof(nonce, "c1", key: new Key())));
    }

    [Fact]
    public void Five_failed_joins_in_a_minute_rate_limit_the_connection()
    {
        for (var i = 0; i < CreationRoomGate.MaxFailedJoins - 1; i++)
            _gate.RecordJoinFailure("c1");
        Assert.False(_gate.IsJoinRateLimited("c1"));

        _gate.RecordJoinFailure("c1");
        Assert.True(_gate.IsJoinRateLimited("c1"));
        Assert.False(_gate.IsJoinRateLimited("c2"));

        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(_gate.IsJoinRateLimited("c1"));
    }

    [Fact]
    public void Forget_drops_challenge_and_failures()
    {
        var nonce = _gate.IssueChallenge("c1");
        for (var i = 0; i < CreationRoomGate.MaxFailedJoins; i++)
            _gate.RecordJoinFailure("c1");

        _gate.Forget("c1");

        Assert.False(_gate.IsJoinRateLimited("c1"));
        Assert.False(_gate.TryConsumeProof("c1", CreationRoomGate.PurposeJoin, "X7K9M2", Pub, Proof(nonce, "c1")));
    }
}

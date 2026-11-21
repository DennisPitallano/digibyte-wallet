using DigiByte.Api.Multisig;
using DigiByte.Crypto.Multisig;
using DigiByte.Crypto.Networks;
using DigiByte.P2P.Shared.Models;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NBitcoin;

namespace DigiByte.Api.Tests;

/// <summary>
/// Rules of online multisig signing rooms (specs/017-multisig-online-signing): T006, T019, T029, T039, T044.
/// </summary>
public class SigningRoomRegistryTests
{
    private static readonly Network Net = DigiByteNetwork.Mainnet;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero));
    private readonly SigningRoomRegistry _rooms;

    public SigningRoomRegistryTests() => _rooms = NewRegistry(new SigningRoomOptions());

    private SigningRoomRegistry NewRegistry(SigningRoomOptions options) => new(_clock, Options.Create(options));

    // ─── Fixtures ───

    private sealed class Multisig
    {
        public required Key[] Keys { get; init; }
        public required PSBT Psbt { get; init; }
        public List<string> Hexes => Keys.Select(k => k.PubKey.ToHex()).ToList();
        public string TxId => Psbt.GetGlobalTransaction().GetHash().ToString();

        public string SignedBy(params Key[] keys)
        {
            var copy = Psbt.Clone();
            foreach (var k in keys) copy.SignWithKeys(k);
            return copy.ToBase64();
        }
    }

    private static Multisig TwoOfThree(bool p2wsh = false)
    {
        var keys = new[] { new Key(), new Key(), new Key() };
        var service = new MultisigService(Net);
        var redeem = service.CreateRedeemScript(2, keys.Select(k => k.PubKey));
        var address = p2wsh ? service.GetP2WSH_Address(redeem) : service.GetP2SH_P2WSH_Address(redeem);
        var coin = new Coin(RandomUtils.GetUInt256(), 0, Money.Coins(10), address.ScriptPubKey);
        var dest = new Key().PubKey.GetAddress(ScriptPubKeyType.Segwit, Net);
        var psbt = service.CreateMultisigPSBTWithFee(redeem, [coin], dest, Money.Coins(1), address, 10_000, 100_000);
        return new Multisig { Keys = keys, Psbt = psbt };
    }

    private string Prove(string conn, Key key, string purpose, string target) =>
        CoSignerProof.Sign(key, CoSignerProof.SigningRoomDomain, purpose, target, _rooms.IssueChallenge(conn), conn);

    private OpenSigningRoomRequest OpenRequest(string conn, Multisig ms, Key? key = null, int? lifetime = null, string? psbt = null,
        List<string>? keys = null, int m = 2, string name = "Alice", string? memo = null)
    {
        key ??= ms.Keys[0];
        return new OpenSigningRoomRequest
        {
            Network = "main",
            CoSignerKeys = keys ?? ms.Hexes,
            RequiredSignatures = m,
            PsbtBase64 = psbt ?? ms.Psbt.ToBase64(),
            Memo = memo,
            LifetimeMinutes = lifetime,
            DisplayName = name,
            PublicKeyHex = key.PubKey.ToHex(),
            Proof = Prove(conn, key, SigningRoomRegistry.PurposeOpen, ms.TxId),
        };
    }

    private string OpenRoom(Multisig ms, string conn = "c-alice")
    {
        var result = _rooms.Open(conn, OpenRequest(conn, ms));
        Assert.Null(result.Error);
        return result.RoomId!;
    }

    private SigningRoomRegistry.JoinResult Join(string conn, string code, Key key, string name = "Bob") =>
        _rooms.Join(conn, new JoinSigningRoomRequest
        {
            InviteCode = code, DisplayName = name, PublicKeyHex = key.PubKey.ToHex(),
            Proof = Prove(conn, key, SigningRoomRegistry.PurposeJoin, code.Trim().ToUpperInvariant()),
        });

    private string Code(string roomId) => _rooms.View(roomId, false)!.InviteCode;

    // ─── Opening (T006) ───

    [Fact]
    public void Open_creates_a_room_with_a_six_character_code_and_the_default_lifetime()
    {
        var roomId = OpenRoom(TwoOfThree());
        var view = _rooms.View(roomId, includePayload: true)!;

        Assert.Equal(6, view.InviteCode.Length);
        Assert.All(view.InviteCode, c => Assert.Contains(c, "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"));
        Assert.Equal(_clock.GetUtcNow().AddMinutes(30), view.ExpiresAt);
        Assert.Equal(SigningRoomStatus.Open, view.Status);
        Assert.NotNull(view.PsbtBase64);
        var creator = Assert.Single(view.Participants);
        Assert.True(creator.IsCreator);
        Assert.True(creator.Online);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Open_accepts_both_address_types(bool p2wsh) => OpenRoom(TwoOfThree(p2wsh));

    [Fact]
    public void A_second_open_by_a_co_signer_of_the_same_payment_returns_the_existing_code()
    {
        var ms = TwoOfThree();
        var roomId = OpenRoom(ms);

        var second = _rooms.Open("c-bob", OpenRequest("c-bob", ms, key: ms.Keys[1], name: "Bob"));

        Assert.Null(second.Error);
        Assert.Equal(Code(roomId), second.ExistingCode);
        Assert.Equal(1, _rooms.RoomCount);
    }

    [Fact]
    public void A_second_open_by_an_outsider_reveals_nothing_and_counts_as_a_failed_join()
    {
        var ms = TwoOfThree();
        var roomId = OpenRoom(ms);
        var outsider = new Key();
        // The outsider lists their own key in place of a real one, so their proof passes, but the payment is not theirs.
        var keys = new List<string> { ms.Hexes[0], ms.Hexes[1], outsider.PubKey.ToHex() };

        var result = _rooms.Open("c-mal", OpenRequest("c-mal", ms, key: outsider, keys: keys, name: "Mallory"));

        Assert.NotNull(result.Error);
        Assert.Null(result.ExistingCode);
        Assert.DoesNotContain(Code(roomId), result.Error!.Message);
    }

    [Fact]
    public void An_open_by_a_co_signer_list_that_does_not_match_the_payment_is_refused()
    {
        var ms = TwoOfThree();
        var outsider = new Key();
        var keys = new List<string> { ms.Hexes[0], ms.Hexes[1], outsider.PubKey.ToHex() };

        var result = _rooms.Open("c1", OpenRequest("c1", ms, keys: keys));

        Assert.Equal(SigningRoomErrors.BadPsbt, result.Error?.Code);
    }

    [Fact]
    public void An_input_missing_its_spent_output_is_refused()
    {
        var ms = TwoOfThree();
        var broken = ms.Psbt.Clone();
        broken.Inputs[0].WitnessUtxo = null;

        var result = _rooms.Open("c1", OpenRequest("c1", ms, psbt: broken.ToBase64()));

        Assert.Equal(SigningRoomErrors.BadPsbt, result.Error?.Code);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(240)]
    [InlineData(2880)]
    public void Lifetimes_outside_the_allowed_choices_are_refused(int minutes)
    {
        var ms = TwoOfThree();
        Assert.Equal(SigningRoomErrors.BadLifetime, _rooms.Open("c1", OpenRequest("c1", ms, lifetime: minutes)).Error?.Code);
    }

    [Fact]
    public void Choices_above_the_operator_maximum_are_neither_offered_nor_accepted()
    {
        var rooms = new SigningRoomRegistry(_clock, Options.Create(new SigningRoomOptions { MaxLifetimeMinutes = 60 }));
        Assert.Equal([15, 30, 60], rooms.Options().AllowedLifetimesMinutes);
        Assert.Equal(30, rooms.Options().DefaultLifetimeMinutes);

        var ms = TwoOfThree();
        var request = new OpenSigningRoomRequest
        {
            Network = "main", CoSignerKeys = ms.Hexes, RequiredSignatures = 2, PsbtBase64 = ms.Psbt.ToBase64(),
            LifetimeMinutes = 120, DisplayName = "A", PublicKeyHex = ms.Hexes[0],
            Proof = CoSignerProof.Sign(ms.Keys[0], CoSignerProof.SigningRoomDomain, "open", ms.TxId, rooms.IssueChallenge("c1"), "c1"),
        };
        Assert.Equal(SigningRoomErrors.BadLifetime, rooms.Open("c1", request).Error?.Code);
    }

    [Fact]
    public void Bad_inputs_are_refused()
    {
        var ms = TwoOfThree();
        Assert.Equal(SigningRoomErrors.BadRequest, _rooms.Open("c1", OpenRequest("c1", ms, memo: new string('x', 201))).Error?.Code);
        Assert.Equal(SigningRoomErrors.BadRequest, _rooms.Open("c1", OpenRequest("c1", ms, name: "   ")).Error?.Code);
        Assert.Equal(SigningRoomErrors.BadRequest, _rooms.Open("c1", OpenRequest("c1", ms, name: new string('n', 41))).Error?.Code);
        Assert.Equal(SigningRoomErrors.BadRequest, _rooms.Open("c1", OpenRequest("c1", ms, keys: [ms.Hexes[0]], m: 1)).Error?.Code);
        Assert.Equal(SigningRoomErrors.BadRequest, _rooms.Open("c1", OpenRequest("c1", ms, m: 4)).Error?.Code);
        Assert.Equal(SigningRoomErrors.BadPsbt, _rooms.Open("c1", OpenRequest("c1", ms, psbt: "not a psbt")).Error?.Code);
        Assert.Equal(SigningRoomErrors.BadPsbt, _rooms.Open("c1", OpenRequest("c1", ms, psbt: Convert.ToBase64String(new byte[100 * 1024 + 1]))).Error?.Code);
    }

    [Fact]
    public void The_opener_must_prove_a_key_of_the_multisig()
    {
        var ms = TwoOfThree();
        var request = OpenRequest("c1", ms);
        request.Proof = CoSignerProof.Sign(new Key(), CoSignerProof.SigningRoomDomain, "open", ms.TxId, _rooms.IssueChallenge("c1"), "c1");

        Assert.Equal(SigningRoomErrors.BadProofOpen, _rooms.Open("c1", request).Error?.Code);
    }

    [Fact]
    public void A_proof_cannot_be_replayed_or_used_after_sixty_seconds()
    {
        var ms = TwoOfThree();
        var request = OpenRequest("c1", ms);
        _clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(SigningRoomErrors.BadProofOpen, _rooms.Open("c1", request).Error?.Code);

        var fresh = OpenRequest("c2", ms);
        Assert.Null(_rooms.Open("c2", fresh).Error);
        Assert.Equal(SigningRoomErrors.BadProofOpen, _rooms.Open("c2", fresh).Error?.Code);
    }

    [Fact]
    public void Expiry_removes_the_room_and_reports_expired()
    {
        var ms = TwoOfThree();
        var roomId = OpenRoom(ms);
        var code = Code(roomId);

        _clock.Advance(TimeSpan.FromMinutes(30));
        var ended = Assert.Single(_rooms.Sweep().Ended);

        Assert.Equal(SigningRoomEndReasons.Expired, ended.Reason);
        Assert.Equal(["c-alice"], ended.Connections);
        Assert.Null(_rooms.View(roomId, false));
        Assert.Equal(SigningRoomErrors.InvalidCode, Join("c-bob", code, ms.Keys[1]).Error?.Code);
    }

    // ─── Joining and signing (T019) ───

    [Fact]
    public void A_co_signer_joins_by_code_in_any_case()
    {
        var ms = TwoOfThree();
        var roomId = OpenRoom(ms);

        var result = Join("c-bob", Code(roomId).ToLowerInvariant(), ms.Keys[1]);

        Assert.Null(result.Error);
        Assert.Equal(2, _rooms.View(roomId, false)!.Participants.Count);
    }

    [Fact]
    public void A_non_member_key_and_an_unknown_code_get_the_same_answer()
    {
        var ms = TwoOfThree();
        var roomId = OpenRoom(ms);

        var outsider = Join("c-mal", Code(roomId), new Key());
        var unknown = Join("c-x", "ZZZZZZ", ms.Keys[1]);

        Assert.Equal(unknown.Error, outsider.Error);
        Assert.Equal(SigningRoomErrors.InvalidCode, outsider.Error!.Code);
        Assert.True(outsider.Error.CountsAsFailedJoin);
        Assert.Single(_rooms.View(roomId, false)!.Participants);
    }

    [Fact]
    public void A_valid_signature_marks_the_signer_and_completes_at_the_threshold()
    {
        var ms = TwoOfThree();
        var roomId = OpenRoom(ms);
        Join("c-bob", Code(roomId), ms.Keys[1]);

        Assert.Null(_rooms.Submit("c-alice", roomId, ms.SignedBy(ms.Keys[0])).Error);
        Assert.Equal(SigningRoomStatus.Open, _rooms.View(roomId, false)!.Status);
        var result = _rooms.Submit("c-bob", roomId, ms.SignedBy(ms.Keys[1]));

        Assert.Null(result.Error);
        var view = _rooms.View(roomId, true)!;
        Assert.Equal(SigningRoomStatus.Complete, view.Status);
        Assert.Equal(2, view.SignedKeys.Count);
        Assert.All(view.Participants, p => Assert.Equal(SigningParticipantAction.Signed, p.Action));
        Assert.Equal(2, view.SignedPsbts.Count);
    }

    [Fact]
    public void A_signature_that_does_not_verify_or_is_for_another_payment_is_refused()
    {
        var ms = TwoOfThree();
        var roomId = OpenRoom(ms);
        var forged = ms.Psbt.Clone();
        forged.Inputs[0].PartialSigs.Add(ms.Keys[0].PubKey, new TransactionSignature(ms.Keys[0].Sign(uint256.One), SigHash.All));
        var other = TwoOfThree();

        Assert.Equal(SigningRoomErrors.BadSignature, _rooms.Submit("c-alice", roomId, forged.ToBase64()).Error?.Code);
        Assert.Equal(SigningRoomErrors.BadSignature, _rooms.Submit("c-alice", roomId, other.SignedBy(other.Keys[0])).Error?.Code);
        Assert.Equal(SigningRoomErrors.BadSignature, _rooms.Submit("c-alice", roomId, ms.Psbt.ToBase64()).Error?.Code);
        Assert.Empty(_rooms.View(roomId, true)!.SignedPsbts);
    }

    [Fact]
    public void A_signature_from_a_key_outside_the_multisig_is_refused()
    {
        var ms = TwoOfThree();
        var roomId = OpenRoom(ms);
        Assert.Equal(SigningRoomErrors.BadSignature, _rooms.Submit("c-alice", roomId, ms.SignedBy(new Key())).Error?.Code);
    }

    [Fact]
    public void Verified_signatures_held_from_another_co_signer_are_accepted_and_counted_once()
    {
        var ms = TwoOfThree();
        var roomId = OpenRoom(ms);
        var copy = ms.SignedBy(ms.Keys[2]);   // Alice imported key 2's signature offline

        _rooms.Submit("c-alice", roomId, copy);
        _rooms.Submit("c-alice", roomId, copy);

        Assert.Equal([ms.Hexes[2].ToLowerInvariant()], _rooms.View(roomId, false)!.SignedKeys);
    }

    [Fact]
    public void Only_room_members_can_submit()
    {
        var ms = TwoOfThree();
        var roomId = OpenRoom(ms);
        Assert.Equal(SigningRoomErrors.RoomEnded, _rooms.Submit("c-stranger", roomId, ms.SignedBy(ms.Keys[1])).Error?.Code);
    }

    [Fact]
    public void Decline_is_refused_after_signing_and_unreachable_is_flagged()
    {
        var ms = TwoOfThree();
        var roomId = OpenRoom(ms);
        Join("c-bob", Code(roomId), ms.Keys[1]);
        Join("c-carol", Code(roomId), ms.Keys[2], "Carol");

        _rooms.Submit("c-alice", roomId, ms.SignedBy(ms.Keys[0]));
        Assert.Equal(SigningRoomErrors.NotAllowed, _rooms.Decline("c-alice", roomId)?.Code);

        Assert.Null(_rooms.Decline("c-bob", roomId));
        Assert.False(_rooms.View(roomId, false)!.Unreachable);
        Assert.Null(_rooms.Decline("c-carol", roomId));
        Assert.True(_rooms.View(roomId, false)!.Unreachable);
    }

    // ─── Broadcasting (T029) ───

    private string CompleteRoom(Multisig ms)
    {
        var roomId = OpenRoom(ms);
        Join("c-bob", Code(roomId), ms.Keys[1]);
        _rooms.Submit("c-alice", roomId, ms.SignedBy(ms.Keys[0]));
        _rooms.Submit("c-bob", roomId, ms.SignedBy(ms.Keys[1]));
        return roomId;
    }

    [Fact]
    public void Only_one_broadcast_claim_is_held_at_a_time()
    {
        var roomId = CompleteRoom(TwoOfThree());

        var first = _rooms.ClaimBroadcast("c-alice", roomId);
        var second = _rooms.ClaimBroadcast("c-bob", roomId);

        Assert.True(first.Granted);
        Assert.False(second.Granted);
        Assert.Equal(first.HolderKey, second.HolderKey);
    }

    [Fact]
    public void A_claim_needs_enough_signatures()
    {
        var ms = TwoOfThree();
        var roomId = OpenRoom(ms);
        Assert.Equal(SigningRoomErrors.NotAllowed, _rooms.ClaimBroadcast("c-alice", roomId).Error?.Code);
    }

    [Fact]
    public void A_claim_is_released_by_a_failed_report_after_sixty_seconds_or_when_the_holder_disconnects()
    {
        var roomId = CompleteRoom(TwoOfThree());

        Assert.True(_rooms.ClaimBroadcast("c-alice", roomId).Granted);
        var failed = _rooms.ReportBroadcast("c-alice", roomId, false, new string('r', 300));
        Assert.Equal(200, failed.FailureReason!.Length);
        Assert.True(_rooms.ClaimBroadcast("c-bob", roomId).Granted);

        _clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Contains(roomId, _rooms.Sweep().ClaimsReleased);
        Assert.True(_rooms.ClaimBroadcast("c-alice", roomId).Granted);

        _rooms.Disconnect("c-alice");
        Assert.True(_rooms.ClaimBroadcast("c-bob", roomId).Granted);
    }

    [Fact]
    public void Only_the_holder_reports_and_success_ends_the_room_after_the_grace_period()
    {
        var roomId = CompleteRoom(TwoOfThree());
        _rooms.ClaimBroadcast("c-alice", roomId);
        var txId = new string('a', 64);

        Assert.Equal(SigningRoomErrors.NotAllowed, _rooms.ReportBroadcast("c-bob", roomId, true, txId).Error?.Code);
        Assert.Equal(txId, _rooms.ReportBroadcast("c-alice", roomId, true, txId.ToUpperInvariant()).TxId);
        Assert.Equal(SigningRoomStatus.Broadcast, _rooms.View(roomId, false)!.Status);
        Assert.Equal(SigningRoomErrors.RoomEnded, _rooms.Submit("c-bob", roomId, "x").Error?.Code);

        _clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Empty(_rooms.Sweep().Ended);
        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(SigningRoomEndReasons.Broadcast, Assert.Single(_rooms.Sweep().Ended).Reason);
    }

    // ─── Disconnecting and rejoining (T039) ───

    [Fact]
    public void A_disconnect_marks_offline_and_keeps_the_signature()
    {
        var ms = TwoOfThree();
        var roomId = OpenRoom(ms);
        Join("c-bob", Code(roomId), ms.Keys[1]);
        _rooms.Submit("c-bob", roomId, ms.SignedBy(ms.Keys[1]));

        _rooms.Disconnect("c-bob");

        var bob = _rooms.View(roomId, false)!.Participants.Single(p => p.PublicKeyHex == ms.Hexes[1].ToLowerInvariant());
        Assert.False(bob.Online);
        Assert.Equal(SigningParticipantAction.Signed, bob.Action);
    }

    [Fact]
    public void The_creator_disconnecting_does_not_end_the_room()
    {
        var ms = TwoOfThree();
        var roomId = OpenRoom(ms);
        Join("c-bob", Code(roomId), ms.Keys[1]);

        _rooms.Disconnect("c-alice");

        Assert.NotNull(_rooms.View(roomId, false));
        Assert.Null(_rooms.Submit("c-bob", roomId, ms.SignedBy(ms.Keys[1])).Error);
    }

    [Fact]
    public void Rejoin_with_a_fresh_proof_moves_the_participant_and_supersedes_the_old_connection()
    {
        var ms = TwoOfThree();
        var roomId = OpenRoom(ms);
        Join("c-bob", Code(roomId), ms.Keys[1]);

        var result = _rooms.Rejoin("c-bob-2", new RejoinSigningRoomRequest
        {
            RoomId = roomId, PublicKeyHex = ms.Hexes[1],
            Proof = Prove("c-bob-2", ms.Keys[1], SigningRoomRegistry.PurposeRejoin, roomId),
        });

        Assert.Null(result.Error);
        Assert.Equal("c-bob", result.SupersededConnection);
        Assert.Null(_rooms.RoomOf("c-bob"));
        Assert.Equal(roomId, _rooms.RoomOf("c-bob-2"));
    }

    [Fact]
    public void Rejoin_needs_a_participant_and_a_valid_proof()
    {
        var ms = TwoOfThree();
        var roomId = OpenRoom(ms);

        var stranger = _rooms.Rejoin("c-x", new RejoinSigningRoomRequest
        {
            RoomId = roomId, PublicKeyHex = ms.Hexes[2],   // a co-signer who never joined
            Proof = Prove("c-x", ms.Keys[2], SigningRoomRegistry.PurposeRejoin, roomId),
        });
        var badProof = _rooms.Rejoin("c-y", new RejoinSigningRoomRequest
        {
            RoomId = roomId, PublicKeyHex = ms.Hexes[0],
            Proof = Prove("c-y", ms.Keys[0], SigningRoomRegistry.PurposeJoin, roomId),
        });

        Assert.Equal(SigningRoomErrors.InvalidCode, stranger.Error?.Code);
        Assert.Equal(SigningRoomErrors.InvalidCode, badProof.Error?.Code);
    }

    [Fact]
    public void Joining_again_with_the_same_key_replaces_the_older_presence()
    {
        var ms = TwoOfThree();
        var roomId = OpenRoom(ms);
        Join("tab-1", Code(roomId), ms.Keys[1]);

        var second = Join("tab-2", Code(roomId), ms.Keys[1]);

        Assert.Equal("tab-1", second.SupersededConnection);
        Assert.Equal(2, _rooms.View(roomId, false)!.Participants.Count);
    }

    // ─── Closing (T044) ───

    [Fact]
    public void Only_the_creator_can_close_and_closing_frees_the_payment_for_a_new_room()
    {
        var ms = TwoOfThree();
        var roomId = OpenRoom(ms);
        var code = Code(roomId);
        Join("c-bob", code, ms.Keys[1]);

        Assert.Equal(SigningRoomErrors.NotAllowed, _rooms.Close("c-bob", roomId).Error?.Code);
        var (error, ended) = _rooms.Close("c-alice", roomId);

        Assert.Null(error);
        Assert.Equal(SigningRoomEndReasons.Closed, ended!.Reason);
        Assert.Equal(SigningRoomErrors.InvalidCode, Join("c-carol", code, ms.Keys[2]).Error?.Code);

        var reopened = _rooms.Open("c-bob", OpenRequest("c-bob", ms, key: ms.Keys[1], name: "Bob"));
        Assert.Null(reopened.Error);
        Assert.NotEqual(code, Code(reopened.RoomId!));
    }

    [Fact]
    public void Views_never_carry_connection_ids()
    {
        var ms = TwoOfThree();
        var roomId = OpenRoom(ms, "secret-connection-id");
        var json = System.Text.Json.JsonSerializer.Serialize(_rooms.View(roomId, true));
        Assert.DoesNotContain("secret-connection-id", json);
    }
}

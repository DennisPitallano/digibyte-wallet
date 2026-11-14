using DigiByte.SpvGateway.Peers;
using Microsoft.Extensions.Caching.Memory;
using NBitcoin;

namespace DigiByte.SpvGateway.Broadcast;

/// <summary>The report of a broadcast (contracts/gateway-api.md, POST /v1/broadcast).</summary>
public sealed record BroadcastResult(string Txid, int RelayedTo, IReadOnlyList<string> Rejections, int SeenBackFrom, string Outcome);

/// <summary>
/// Offers a signed transaction to several peers (spec FR-011, R8). Peers do not confirm acceptance, so the report is what happened:
/// how many peers took it, which ones rejected it and why, and how many announced it back. <c>accepted</c> means at least one peer took
/// it and none of the peers that took it rejected it (or another peer announced it back); <c>rejected</c> means peers rejected it and
/// nobody announced it back; <c>no_response</c> means no peer took it. Sending the same transaction again returns the earlier
/// answer without sending it again.
/// </summary>
public sealed class BroadcastRelay
{
    public const int MinimumPeers = 4;
    public const int MaxPeers = 8;
    public const int MaxTransactionBytes = 400_000;

    private readonly PeerManager _peers;
    private readonly Network _network;
    private readonly TimeSpan _seenBackWait;
    private readonly MemoryCache _recent = new(new MemoryCacheOptions { SizeLimit = 1000 });

    public BroadcastRelay(PeerManager peers, Network network, TimeSpan? seenBackWait = null)
    {
        _peers = peers;
        _network = network;
        _seenBackWait = seenBackWait ?? TimeSpan.FromSeconds(3);
    }

    /// <exception cref="FormatException">The bytes are not a transaction with inputs and outputs.</exception>
    public async Task<BroadcastResult> RelayAsync(byte[] raw, CancellationToken cancellationToken)
    {
        var transaction = Parse(raw);
        var txid = transaction.GetHash();

        if (_recent.TryGetValue(txid, out BroadcastResult? earlier) && earlier is not null && earlier.Outcome != "no_response")
            return earlier;

        var targets = _peers.Sessions
            .Where(s => s.IsConnected)
            .OrderByDescending(s => (s.Services & PeerRecord.NodeNetwork) != 0)
            .Take(MaxPeers)
            .ToList();

        var outcomes = await Task.WhenAll(targets.Select(async peer =>
        {
            try
            {
                return (Peer: peer, Outcome: await peer.RelayTransactionAsync(txid, raw, cancellationToken));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                return (Peer: peer, Outcome: new PeerRelayOutcome(false, null));
            }
        }));

        var delivered = outcomes.Where(o => o.Outcome.Delivered).ToList();
        var rejections = outcomes.Where(o => o.Outcome.RejectReason is not null).Select(o => o.Outcome.RejectReason!).ToList();

        if (_seenBackWait > TimeSpan.Zero && delivered.Count > 0)
            await Task.Delay(_seenBackWait, cancellationToken);
        var seenBack = _peers.Sessions.Count(s => s.HasAnnounced(txid));

        string outcome;
        if (delivered.Count == 0)
            outcome = "no_response";
        else if (seenBack > 0 || rejections.Count < delivered.Count && !delivered.All(o => o.Outcome.RejectReason is not null))
            outcome = "accepted";
        else
            outcome = "rejected";

        var result = new BroadcastResult(txid.ToString(), delivered.Count, rejections, seenBack, outcome);
        if (outcome != "no_response")
            _recent.Set(txid, result, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) });
        return result;
    }

    private Transaction Parse(byte[] raw)
    {
        if (raw.Length == 0 || raw.Length > MaxTransactionBytes)
            throw new FormatException("The transaction is empty or too large.");
        Transaction transaction;
        try
        {
            transaction = Transaction.Load(raw, _network);
        }
        catch (Exception)
        {
            throw new FormatException("The bytes are not a transaction.");
        }
        if (transaction.Inputs.Count == 0 || transaction.Outputs.Count == 0)
            throw new FormatException("The transaction has no inputs or no outputs.");
        return transaction;
    }
}

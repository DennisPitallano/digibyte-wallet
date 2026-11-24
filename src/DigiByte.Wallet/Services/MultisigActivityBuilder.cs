using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>
/// Turns a multisig's chain transactions and its local proposals into one activity list. Pure: no storage, no network.
/// </summary>
public static class MultisigActivityBuilder
{
    /// <summary>
    /// Builds the list, newest first. A proposal that was broadcast and is also on chain appears once, as Sent.
    /// </summary>
    public static List<ActivityEntry> Build(
        string multisigAddress,
        IEnumerable<TransactionInfo> chainTransactions,
        IEnumerable<PendingMultisigTransaction> proposals)
    {
        var proposalList = proposals.ToList();
        var byTxId = proposalList
            .Where(p => !string.IsNullOrEmpty(p.BroadcastTxId))
            .GroupBy(p => p.BroadcastTxId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var entries = new List<ActivityEntry>();
        var onChain = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tx in chainTransactions)
        {
            var entry = FromChain(multisigAddress, tx, byTxId.GetValueOrDefault(tx.TxId));
            if (entry is null) continue;
            onChain.Add(tx.TxId);
            entries.Add(entry);
        }

        foreach (var proposal in proposalList)
        {
            if (proposal.Status == PendingTxStatus.Broadcast)
            {
                // Already shown from the chain; otherwise keep it visible as Sent until the chain reports it.
                if (!string.IsNullOrEmpty(proposal.BroadcastTxId) && onChain.Contains(proposal.BroadcastTxId)) continue;
                entries.Add(FromProposal(proposal, ActivityKind.Sent, ActivityDirection.Out));
                continue;
            }

            var kind = proposal.Status switch
            {
                PendingTxStatus.AwaitingSignatures => ActivityKind.ProposalAwaiting,
                PendingTxStatus.ReadyToFinalize => ActivityKind.ProposalReady,
                PendingTxStatus.Failed => ActivityKind.Failed,
                PendingTxStatus.Cancelled => ActivityKind.Cancelled,
                _ => ActivityKind.ProposalAwaiting,
            };
            entries.Add(FromProposal(proposal, kind, ActivityDirection.None));
        }

        return entries.OrderByDescending(e => e.Timestamp).ToList();
    }

    /// <summary>Keeps only the entries a filter chip asks for.</summary>
    public static List<ActivityEntry> Filter(IEnumerable<ActivityEntry> entries, ActivityFilter filter) =>
        entries.Where(e => filter switch
        {
            ActivityFilter.All => true,
            ActivityFilter.Received => e.Kind == ActivityKind.Received,
            ActivityFilter.Sent => e.Kind == ActivityKind.Sent,
            ActivityFilter.Pending => e.IsPending,
            ActivityFilter.Failed => e.IsFailed,
            _ => true,
        }).ToList();

    /// <summary>Groups newest-first entries by calendar day (local time), keeping their order.</summary>
    public static List<(DateOnly Date, List<ActivityEntry> Entries)> GroupByDate(IEnumerable<ActivityEntry> entries) =>
        entries
            .GroupBy(e => DateOnly.FromDateTime(e.Timestamp.Kind == DateTimeKind.Utc ? e.Timestamp.ToLocalTime() : e.Timestamp))
            .OrderByDescending(g => g.Key)
            .Select(g => (g.Key, g.ToList()))
            .ToList();

    private static ActivityEntry? FromChain(string address, TransactionInfo tx, PendingMultisigTransaction? proposal)
    {
        bool Mine(string? a) => string.Equals(a, address, StringComparison.Ordinal);

        var spentFromMe = tx.Inputs.Any(i => Mine(i.Address));
        var paidToMe = tx.Outputs.Where(o => Mine(o.Address)).Sum(o => o.AmountSatoshis);

        if (spentFromMe)
        {
            var external = tx.Outputs.Where(o => !Mine(o.Address) && !string.IsNullOrEmpty(o.Address) && o.AmountSatoshis > 0).ToList();
            var distinct = external.Select(o => o.Address).Distinct().ToList();
            return new ActivityEntry
            {
                Kind = ActivityKind.Sent,
                Direction = ActivityDirection.Out,
                TxId = tx.TxId,
                AmountSatoshis = external.Sum(o => o.AmountSatoshis),
                FeeSatoshis = tx.FeeSatoshis,
                Counterparty = distinct.FirstOrDefault(),
                MoreCount = Math.Max(0, distinct.Count - 1),
                Recipients = external.Select(o => new ProposalRecipient(o.Address, o.AmountSatoshis)).ToList(),
                Memo = proposal?.Memo,
                Timestamp = tx.Timestamp,
                Confirmations = tx.Confirmations,
                ProposalId = proposal?.Id,
            };
        }

        if (paidToMe > 0)
        {
            var senders = tx.Inputs.Select(i => i.Address).Where(a => !string.IsNullOrEmpty(a)).Distinct().ToList();
            return new ActivityEntry
            {
                Kind = ActivityKind.Received,
                Direction = ActivityDirection.In,
                TxId = tx.TxId,
                AmountSatoshis = paidToMe,
                Counterparty = senders.FirstOrDefault(),
                MoreCount = Math.Max(0, senders.Count - 1),
                Timestamp = tx.Timestamp,
                Confirmations = tx.Confirmations,
            };
        }

        return null;
    }

    private static ActivityEntry FromProposal(PendingMultisigTransaction p, ActivityKind kind, ActivityDirection direction) => new()
    {
        Kind = kind,
        Direction = direction,
        TxId = p.BroadcastTxId,
        AmountSatoshis = p.AmountSatoshis,
        FeeSatoshis = p.FeeSatoshis,
        Counterparty = p.DestinationAddress,
        MoreCount = Math.Max(0, p.RecipientCount - 1),
        Recipients = p.AllRecipients,
        Memo = p.Memo,
        Timestamp = kind == ActivityKind.Cancelled ? p.CancelledAt ?? p.CreatedAt : p.CreatedAt,
        Confirmations = kind == ActivityKind.Sent ? 0 : null,
        ProposalId = p.Id,
        SignedCount = p.SignatureCount,
        RequiredCount = p.RequiredSignatures,
    };
}

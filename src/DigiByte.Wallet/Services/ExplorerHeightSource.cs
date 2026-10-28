namespace DigiByte.Wallet.Services;

/// <summary>
/// The chain height from a public explorer, used to cross-check our own node (specs/005-wallet-own-node-source, research R7).
/// It calls each explorer's own height method directly (not the cached fallback wrapper) and gives the whole call a hard time
/// budget: the explorer clients retry for up to 45 seconds, which must never stretch a readiness check. Never throws.
/// </summary>
public sealed class ExplorerHeightSource : IExplorerHeightSource
{
    private readonly IReadOnlyList<IBlockchainService> _explorers;
    private readonly TimeSpan _budget;

    public ExplorerHeightSource(IEnumerable<IBlockchainService> explorers, TimeSpan? budget = null)
    {
        _explorers = explorers.ToList();
        _budget = budget ?? TimeSpan.FromSeconds(5);
    }

    public async Task<int?> GetHeightAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_budget);

            foreach (var explorer in _explorers)
            {
                try
                {
                    // WaitAsync abandons waiting when the budget ends; the explorer call itself may carry on in the background.
                    var height = await explorer.GetBlockHeightAsync().WaitAsync(timeout.Token);
                    if (height > 0)
                        return height;
                }
                catch (OperationCanceledException)
                {
                    return null;   // out of time (or cancelled): no answer
                }
                catch (Exception)
                {
                    // this explorer failed; try the next one
                }
            }
        }
        catch (Exception)
        {
            // never throw into the readiness check
        }
        return null;
    }
}

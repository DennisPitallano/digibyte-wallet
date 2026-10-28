using DigiByte.Wallet.Services;

namespace DigiByte.Web.Services;

/// <summary>The public explorer backends in their normal order, shared by the fallback service and the node height cross-check.</summary>
public sealed record ExplorerBackends(IReadOnlyList<IBlockchainService> All);

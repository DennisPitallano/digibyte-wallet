using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>
/// What the wallet's oracle client offers to the rest of the wallet (specs/014-digibyte-io-oracle, data-model.md), so nothing has to wait for the
/// network: the latest reading it holds, whether the first fetch is still on its way, a way to ask for a refresh in the background, and an event
/// for when a fetch ends. None of the members throws or blocks.
/// </summary>
public interface IOracleFeed
{
    /// <summary>The latest valid reading whose <c>FetchedAt</c> is at most 5 minutes old, or null.</summary>
    OracleReading? Current { get; }

    /// <summary>True only while the first fetch is in flight and there is no current reading (so a normal load shows "loading", not an error).</summary>
    bool IsLoading { get; }

    /// <summary>
    /// Starts a background fetch only when none is in flight, at least 30 seconds have passed since the previous attempt started (whether it worked
    /// or not), and the current answer, if any, is more than 30 seconds old. Otherwise does nothing.
    /// </summary>
    void RequestRefresh();

    /// <summary>Raised when a fetch ends, successfully or not, so screens can re-read the health at once.</summary>
    event Action? Changed;
}

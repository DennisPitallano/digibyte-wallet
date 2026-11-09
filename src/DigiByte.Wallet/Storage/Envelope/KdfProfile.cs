namespace DigiByte.Wallet.Storage.Envelope;

/// <summary>Argon2id cost settings stored in each envelope header.</summary>
public sealed record KdfParameters(int MemoryKiB, int Passes, int Lanes);

/// <summary>
/// The named Argon2id profiles and the floor below which nothing is ever written or accepted.
/// Values are provisional starting points from research R3 ("to be frozen" once the T001 benchmark has run).
/// </summary>
public static class KdfProfile
{
    public const int FloorMemoryKiB = 19_456;
    public const int FloorPasses = 2;
    public const int RequiredLanes = 1;

    public static KdfParameters Standard { get; } = new(65_536, 3, 1);
    public static KdfParameters LowMemory { get; } = new(32_768, 3, 1);
    public static KdfParameters Floor { get; } = new(FloorMemoryKiB, FloorPasses, RequiredLanes);

    public static bool IsAtOrAboveFloor(KdfParameters p) =>
        p.MemoryKiB >= FloorMemoryKiB && p.Passes >= FloorPasses && p.Lanes == RequiredLanes;
}

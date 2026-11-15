namespace DigiByte.Wallet.Services;

/// <summary>The pieces the wallet service needs to restore through the gateway, handed over as one optional dependency.</summary>
public sealed record GatewayRestoreServices(BirthHeightService Birth, GatewayRestoreScanner Scanner, ScanProgressStore Progress);

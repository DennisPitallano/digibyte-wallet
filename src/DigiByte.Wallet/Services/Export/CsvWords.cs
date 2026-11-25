namespace DigiByte.Wallet.Services.Export;

/// <summary>
/// The header names and Type/Status words of the file, in the app's language (FR-018, contracts/csv-file-format.md). They live
/// here, not in the web app's locale files, so the column contract is one tested table.
/// </summary>
public sealed class CsvWords
{
    public required string Date { get; init; }
    public required string TimeZone { get; init; }
    public required string Asset { get; init; }
    public required string Type { get; init; }
    public required string Amount { get; init; }
    public required string Fee { get; init; }
    public required string Price { get; init; }
    public required string Value { get; init; }
    public required string Daily { get; init; }
    public required string ViaUsd { get; init; }
    public required string Status { get; init; }
    public required string Confirmations { get; init; }
    public required string Name { get; init; }
    public required string Counterparty { get; init; }
    public required string WalletAddress { get; init; }
    public required string Note { get; init; }
    public required string TransactionId { get; init; }
    public required IReadOnlyDictionary<ExportKind, string> Kinds { get; init; }
    public required IReadOnlyDictionary<ExportStatus, string> Statuses { get; init; }

    public static CsvWords English { get; } = new()
    {
        Date = "Date",
        TimeZone = "Time zone",
        Asset = "Asset",
        Type = "Type",
        Amount = "Amount",
        Fee = "Fee (DGB)",
        Price = "Price",
        Value = "Value",
        Daily = "daily",
        ViaUsd = "via USD",
        Status = "Status",
        Confirmations = "Confirmations",
        Name = "Name",
        Counterparty = "Counterparty address",
        WalletAddress = "Wallet address",
        Note = "Note",
        TransactionId = "Transaction ID",
        Kinds = new Dictionary<ExportKind, string>
        {
            [ExportKind.Received] = "Received",
            [ExportKind.Sent] = "Sent",
            [ExportKind.OwnTransfer] = "Own transfer",
            [ExportKind.DigiDollarTransfer] = "DigiDollar transfer",
            [ExportKind.DigiDollarMinted] = "DigiDollar minted",
            [ExportKind.DigiDollarRedeemed] = "DigiDollar redeemed",
        },
        Statuses = new Dictionary<ExportStatus, string>
        {
            [ExportStatus.Confirmed] = "Confirmed",
            [ExportStatus.Unconfirmed] = "Unconfirmed",
            [ExportStatus.Failed] = "Failed",
        },
    };

    public static CsvWords Spanish { get; } = new()
    {
        Date = "Fecha",
        TimeZone = "Zona horaria",
        Asset = "Activo",
        Type = "Tipo",
        Amount = "Importe",
        Fee = "Comisión (DGB)",
        Price = "Precio",
        Value = "Valor",
        Daily = "diario",
        ViaUsd = "vía USD",
        Status = "Estado",
        Confirmations = "Confirmaciones",
        Name = "Nombre",
        Counterparty = "Dirección de la otra parte",
        WalletAddress = "Dirección de la billetera",
        Note = "Nota",
        TransactionId = "ID de transacción",
        Kinds = new Dictionary<ExportKind, string>
        {
            [ExportKind.Received] = "Recibido",
            [ExportKind.Sent] = "Enviado",
            [ExportKind.OwnTransfer] = "Transferencia propia",
            [ExportKind.DigiDollarTransfer] = "Transferencia DigiDollar",
            [ExportKind.DigiDollarMinted] = "DigiDollar emitido",
            [ExportKind.DigiDollarRedeemed] = "DigiDollar canjeado",
        },
        Statuses = new Dictionary<ExportStatus, string>
        {
            [ExportStatus.Confirmed] = "Confirmado",
            [ExportStatus.Unconfirmed] = "Sin confirmar",
            [ExportStatus.Failed] = "Fallido",
        },
    };

    /// <summary>The words for the app language (<c>es</c>, <c>es-MX</c>… → Spanish; anything else → English).</summary>
    public static CsvWords For(string? language) =>
        language is not null && language.StartsWith("es", StringComparison.OrdinalIgnoreCase) ? Spanish : English;

    /// <summary><c>Price (USD, daily)</c>, or <c>Price (KES, daily, via USD)</c> for a converted currency.</summary>
    public string PriceHeader(string currency, PriceBasis basis) => Header(Price, currency, basis);

    public string ValueHeader(string currency, PriceBasis basis) => Header(Value, currency, basis);

    private string Header(string word, string currency, PriceBasis basis) =>
        basis == PriceBasis.ConvertedFromUsd
            ? $"{word} ({currency.ToUpperInvariant()}, {Daily}, {ViaUsd})"
            : $"{word} ({currency.ToUpperInvariant()}, {Daily})";
}

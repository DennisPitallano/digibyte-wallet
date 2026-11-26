namespace DigiByte.Pay.Api.Data;

/// <summary>
/// One-off backfill for <see cref="PayApiKey.Kind"/> / <see cref="PayApiKey.StoreId"/>, run once
/// when the columns are added: by the AddApiKeyKindAndSimulatedSessions migration (Postgres) and by the
/// SQLite schema shim in Program.cs. It must not run again later, or a merchant key someone
/// labels "POS …" would turn into a tablet key.
///
/// Before this, a POS tablet key was any key whose label starts with "POS" (case-sensitive,
/// matching the old StartsWith filter). Pairing records only ever lived in memory, so the
/// store a tablet was paired for is not on record; the one safe derivation is a merchant
/// with exactly one store — the tablet can only have been paired for that store. Every
/// other legacy tablet key keeps StoreId null and its old account-wide access.
///
/// Plain SQL that runs unchanged on Postgres and SQLite (substr is in both; LIKE is not
/// case-sensitive on SQLite).
/// </summary>
public static class ApiKeyKindBackfill
{
    public const string Sql = """
        UPDATE "ApiKeys" SET "Kind" = 'pos'
        WHERE "Label" IS NOT NULL AND substr("Label", 1, 3) = 'POS';

        UPDATE "ApiKeys" SET "StoreId" = (
            SELECT MIN("Stores"."Id") FROM "Stores" WHERE "Stores"."MerchantId" = "ApiKeys"."MerchantId")
        WHERE "Kind" = 'pos' AND "StoreId" IS NULL
          AND (SELECT COUNT(*) FROM "Stores" WHERE "Stores"."MerchantId" = "ApiKeys"."MerchantId") = 1;
        """;
}

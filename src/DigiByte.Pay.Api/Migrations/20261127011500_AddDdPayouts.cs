using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigiByte.Pay.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDdPayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PayloadJson",
                table: "WebhookDeliveries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayoutId",
                table: "WebhookDeliveries",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DdPayoutAddress",
                table: "Stores",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DdPayoutAddressRequiredExempt",
                table: "Stores",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DdPayoutAddressSetAt",
                table: "Stores",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DdPayoutConsecutiveFailures",
                table: "Stores",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "DdPayoutDailyTime",
                table: "Stores",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DdPayoutLastDailyRunAt",
                table: "Stores",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DdPayoutRequestReason",
                table: "Stores",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DdPayoutRequestedAt",
                table: "Stores",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DdPayoutSchedule",
                table: "Stores",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "confirmed");

            migrationBuilder.AddColumn<long>(
                name: "DdPayoutThresholdCents",
                table: "Stores",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DdPayoutTimeZone",
                table: "Stores",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DdPayoutsAutoPausedAt",
                table: "Stores",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DdPayoutsPausedByOperatorAt",
                table: "Stores",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DdAddresses",
                columns: table => new
                {
                    Address = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MerchantId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Network = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DdAddresses", x => x.Address);
                });

            migrationBuilder.CreateTable(
                name: "DdLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MerchantId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    AmountCents = table.Column<long>(type: "bigint", nullable: false),
                    Txid = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Address = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    PayoutId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Confirmations = table.Column<int>(type: "integer", nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReversedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DdLedgerEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DdPayoutItems",
                columns: table => new
                {
                    PayoutId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LedgerEntryId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AmountCents = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DdPayoutItems", x => new { x.PayoutId, x.LedgerEntryId });
                });

            migrationBuilder.CreateTable(
                name: "DdPayouts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MerchantId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Destination = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AmountCents = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    WaitReason = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Trigger = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Txid = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Confirmations = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CoverageWaitSince = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DdPayouts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MerchantDdSettings",
                columns: table => new
                {
                    MerchantId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Network = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DdPayoutAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DdPayoutAddressSetAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DdPayoutSchedule = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DdPayoutDailyTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    DdPayoutTimeZone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DdPayoutThresholdCents = table.Column<long>(type: "bigint", nullable: true),
                    DdPayoutsAutoPausedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DdPayoutConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    DdPayoutsPausedByOperatorAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DdPayoutRequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DdPayoutRequestReason = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    DdPayoutLastDailyRunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MerchantDdSettings", x => x.MerchantId);
                });

            migrationBuilder.CreateTable(
                name: "OperatorSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Value = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperatorSettings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "PayoutAddressChanges",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    MerchantId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OldAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    NewAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RequestedBySessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledBySessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    AppliedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcknowledgedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayoutAddressChanges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StepUpGrants",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MerchantId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DigiIdAddress = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StepUpGrants", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DdAddresses_MerchantId_StoreId",
                table: "DdAddresses",
                columns: new[] { "MerchantId", "StoreId" });

            migrationBuilder.CreateIndex(
                name: "IX_DdAddresses_SessionId",
                table: "DdAddresses",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_DdLedgerEntries_MerchantId_StoreId",
                table: "DdLedgerEntries",
                columns: new[] { "MerchantId", "StoreId" });

            migrationBuilder.CreateIndex(
                name: "IX_DdLedgerEntries_PayoutId",
                table: "DdLedgerEntries",
                column: "PayoutId");

            migrationBuilder.CreateIndex(
                name: "IX_DdLedgerEntries_SessionId",
                table: "DdLedgerEntries",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "UX_DdLedger_Txid_Address_Source",
                table: "DdLedgerEntries",
                columns: new[] { "Txid", "Address", "Source" },
                unique: true,
                filter: "\"Type\" = 'credit'");

            migrationBuilder.CreateIndex(
                name: "IX_DdPayoutItems_LedgerEntryId",
                table: "DdPayoutItems",
                column: "LedgerEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_DdPayouts_MerchantId_CreatedAt",
                table: "DdPayouts",
                columns: new[] { "MerchantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DdPayouts_StoreId_Status",
                table: "DdPayouts",
                columns: new[] { "StoreId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PayoutAddressChanges_MerchantId_Status",
                table: "PayoutAddressChanges",
                columns: new[] { "MerchantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PayoutAddressChanges_StoreId_Status",
                table: "PayoutAddressChanges",
                columns: new[] { "StoreId", "Status" });

            // specs/034 FR-031: stores that already had DigiDollar on keep it on without a payout address.
            migrationBuilder.Sql("UPDATE \"Stores\" SET \"DdPayoutAddressRequiredExempt\" = TRUE WHERE \"DigiDollarEnabled\" = TRUE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DdAddresses");

            migrationBuilder.DropTable(
                name: "DdLedgerEntries");

            migrationBuilder.DropTable(
                name: "DdPayoutItems");

            migrationBuilder.DropTable(
                name: "DdPayouts");

            migrationBuilder.DropTable(
                name: "MerchantDdSettings");

            migrationBuilder.DropTable(
                name: "OperatorSettings");

            migrationBuilder.DropTable(
                name: "PayoutAddressChanges");

            migrationBuilder.DropTable(
                name: "StepUpGrants");

            migrationBuilder.DropColumn(
                name: "PayloadJson",
                table: "WebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "PayoutId",
                table: "WebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "DdPayoutAddress",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "DdPayoutAddressRequiredExempt",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "DdPayoutAddressSetAt",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "DdPayoutConsecutiveFailures",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "DdPayoutDailyTime",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "DdPayoutLastDailyRunAt",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "DdPayoutRequestReason",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "DdPayoutRequestedAt",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "DdPayoutSchedule",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "DdPayoutThresholdCents",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "DdPayoutTimeZone",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "DdPayoutsAutoPausedAt",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "DdPayoutsPausedByOperatorAt",
                table: "Stores");
        }
    }
}

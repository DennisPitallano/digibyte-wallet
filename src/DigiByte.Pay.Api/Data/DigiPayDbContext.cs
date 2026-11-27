using Microsoft.EntityFrameworkCore;

namespace DigiByte.Pay.Api.Data;

public class DigiPayDbContext : DbContext
{
    public DigiPayDbContext(DbContextOptions<DigiPayDbContext> options) : base(options) { }

    public DbSet<PayMerchant> Merchants => Set<PayMerchant>();
    public DbSet<PayStore> Stores => Set<PayStore>();
    public DbSet<PaySession> Sessions => Set<PaySession>();
    public DbSet<MerchantSession> MerchantSessions => Set<MerchantSession>();
    public DbSet<PayApiKey> ApiKeys => Set<PayApiKey>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<PayPosPairing> PosPairings => Set<PayPosPairing>();
    public DbSet<PaySessionOption> SessionOptions => Set<PaySessionOption>();
    // DD payouts (specs/034).
    public DbSet<DdAddress> DdAddresses => Set<DdAddress>();
    public DbSet<DdLedgerEntry> DdLedgerEntries => Set<DdLedgerEntry>();
    public DbSet<DdPayout> DdPayouts => Set<DdPayout>();
    public DbSet<DdPayoutItem> DdPayoutItems => Set<DdPayoutItem>();
    public DbSet<PayoutAddressChange> PayoutAddressChanges => Set<PayoutAddressChange>();
    public DbSet<StepUpGrant> StepUpGrants => Set<StepUpGrant>();
    public DbSet<OperatorSetting> OperatorSettings => Set<OperatorSetting>();
    public DbSet<MerchantDdSettings> MerchantDdSettings => Set<MerchantDdSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PayMerchant>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasIndex(m => m.ApiKeyPrefix).IsUnique();
            e.HasIndex(m => m.DigiIdAddress);
            e.Property(m => m.DigiIdAddress).HasMaxLength(80);
            e.Property(m => m.DisplayName).HasMaxLength(120).IsRequired();
            e.Property(m => m.ApiKeyPrefix).HasMaxLength(32).IsRequired();
            e.Property(m => m.ApiKeyHash).HasMaxLength(128).IsRequired();
        });

        modelBuilder.Entity<PayStore>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => s.MerchantId);
            e.Property(s => s.MerchantId).HasMaxLength(32).IsRequired();
            e.Property(s => s.Name).HasMaxLength(120).IsRequired();
            e.Property(s => s.Network).HasMaxLength(20).IsRequired();
            e.Property(s => s.Xpub).HasMaxLength(200);
            e.Property(s => s.ReceiveAddress).HasMaxLength(80);
            e.Property(s => s.DefaultCurrency).HasMaxLength(8);
            e.Property(s => s.BuyerChoiceEnabled).HasDefaultValue(true);
            e.Property(s => s.DdDonationAddress).HasMaxLength(120);
            e.Ignore(s => s.OwnerStoreId);
            e.Property(s => s.DdPayoutAddress).HasMaxLength(128);
            e.Property(s => s.DdPayoutSchedule).HasMaxLength(16).IsRequired().HasDefaultValue("confirmed");
            e.Property(s => s.DdPayoutTimeZone).HasMaxLength(64);
            e.Property(s => s.DdPayoutRequestReason).HasMaxLength(16);
            e.Property(s => s.DdPayoutAddressRequiredExempt).HasDefaultValue(false);
        });

        modelBuilder.Entity<DdAddress>(e =>
        {
            e.ToTable("DdAddresses");
            e.HasKey(a => a.Address);
            e.Property(a => a.Address).HasMaxLength(128);
            e.Property(a => a.MerchantId).HasMaxLength(32).IsRequired();
            e.Property(a => a.StoreId).HasMaxLength(32);
            e.Property(a => a.Kind).HasMaxLength(16).IsRequired();
            e.Property(a => a.SessionId).HasMaxLength(32);
            e.Property(a => a.Network).HasMaxLength(20).IsRequired();
            e.HasIndex(a => new { a.MerchantId, a.StoreId });
            e.HasIndex(a => a.SessionId);
        });

        modelBuilder.Entity<DdLedgerEntry>(e =>
        {
            e.ToTable("DdLedgerEntries");
            e.HasKey(l => l.Id);
            e.Property(l => l.Id).HasMaxLength(32);
            e.Property(l => l.MerchantId).HasMaxLength(32).IsRequired();
            e.Property(l => l.StoreId).HasMaxLength(32);
            e.Property(l => l.Type).HasMaxLength(24).IsRequired();
            e.Property(l => l.Source).HasMaxLength(16);
            e.Property(l => l.Txid).HasMaxLength(80);
            e.Property(l => l.Address).HasMaxLength(128);
            e.Property(l => l.SessionId).HasMaxLength(32);
            e.Property(l => l.PayoutId).HasMaxLength(32);
            e.HasIndex(l => new { l.MerchantId, l.StoreId });
            e.HasIndex(l => l.SessionId);
            e.HasIndex(l => l.PayoutId);
            // A receive is credited once (research R4).
            e.HasIndex(l => new { l.Txid, l.Address, l.Source }).IsUnique().HasDatabaseName("UX_DdLedger_Txid_Address_Source")
                .HasFilter("\"Type\" = 'credit'");
        });

        modelBuilder.Entity<DdPayout>(e =>
        {
            e.ToTable("DdPayouts");
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).HasMaxLength(32);
            e.Property(p => p.MerchantId).HasMaxLength(32).IsRequired();
            e.Property(p => p.StoreId).HasMaxLength(32);
            e.Property(p => p.Destination).HasMaxLength(128).IsRequired();
            e.Property(p => p.Status).HasMaxLength(16).IsRequired();
            e.Property(p => p.WaitReason).HasMaxLength(24);
            e.Property(p => p.FailureCode).HasMaxLength(40);
            e.Property(p => p.FailureReason).HasMaxLength(512);
            e.Property(p => p.Trigger).HasMaxLength(16).IsRequired();
            e.Property(p => p.Txid).HasMaxLength(80);
            e.HasIndex(p => new { p.StoreId, p.Status });
            e.HasIndex(p => new { p.MerchantId, p.CreatedAt });
        });

        modelBuilder.Entity<DdPayoutItem>(e =>
        {
            e.ToTable("DdPayoutItems");
            e.HasKey(i => new { i.PayoutId, i.LedgerEntryId });
            e.Property(i => i.PayoutId).HasMaxLength(32);
            e.Property(i => i.LedgerEntryId).HasMaxLength(32);
            e.HasIndex(i => i.LedgerEntryId);
        });

        modelBuilder.Entity<PayoutAddressChange>(e =>
        {
            e.ToTable("PayoutAddressChanges");
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).HasMaxLength(32);
            e.Property(c => c.StoreId).HasMaxLength(32);
            e.Property(c => c.MerchantId).HasMaxLength(32).IsRequired();
            e.Property(c => c.OldAddress).HasMaxLength(128);
            e.Property(c => c.NewAddress).HasMaxLength(128).IsRequired();
            e.Property(c => c.Status).HasMaxLength(16).IsRequired();
            e.Property(c => c.RequestedBySessionId).HasMaxLength(32).IsRequired();
            e.Property(c => c.CancelledBySessionId).HasMaxLength(32);
            e.HasIndex(c => new { c.StoreId, c.Status });
            e.HasIndex(c => new { c.MerchantId, c.Status });
        });

        modelBuilder.Entity<StepUpGrant>(e =>
        {
            e.ToTable("StepUpGrants");
            e.HasKey(g => g.Id);
            e.Property(g => g.Id).HasMaxLength(32);
            e.Property(g => g.MerchantId).HasMaxLength(32).IsRequired();
            e.Property(g => g.SessionId).HasMaxLength(32).IsRequired();
            e.Property(g => g.Purpose).HasMaxLength(32).IsRequired();
            e.Property(g => g.DigiIdAddress).HasMaxLength(80).IsRequired();
        });

        modelBuilder.Entity<OperatorSetting>(e =>
        {
            e.ToTable("OperatorSettings");
            e.HasKey(o => o.Key);
            e.Property(o => o.Key).HasMaxLength(64);
            e.Property(o => o.Value).HasMaxLength(512);
        });

        modelBuilder.Entity<MerchantDdSettings>(e =>
        {
            e.ToTable("MerchantDdSettings");
            e.HasKey(m => m.MerchantId);
            e.Ignore(m => m.OwnerStoreId);
            e.Property(m => m.MerchantId).HasMaxLength(32);
            e.Property(m => m.Network).HasMaxLength(20).IsRequired();
            e.Property(m => m.DdPayoutAddress).HasMaxLength(128);
            e.Property(m => m.DdPayoutSchedule).HasMaxLength(16).IsRequired();
            e.Property(m => m.DdPayoutTimeZone).HasMaxLength(64);
            e.Property(m => m.DdPayoutRequestReason).HasMaxLength(16);
        });

        modelBuilder.Entity<PaySession>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => s.MerchantId);
            e.HasIndex(s => s.StoreId);
            e.HasIndex(s => s.Status);
            e.HasIndex(s => s.Address);
            e.Property(s => s.MerchantId).HasMaxLength(32).IsRequired();
            e.Property(s => s.StoreId).HasMaxLength(32).IsRequired();
            e.Property(s => s.Address).HasMaxLength(80).IsRequired();
            e.Property(s => s.Label).HasMaxLength(200);
            e.Property(s => s.Memo).HasMaxLength(200);
            e.Property(s => s.FiatCurrency).HasMaxLength(10);
            e.Property(s => s.PaidTxid).HasMaxLength(80);
            e.Property(s => s.Source).HasMaxLength(32);
            e.Property(s => s.RefundTxid).HasMaxLength(128);
            e.Property(s => s.RefundNote).HasMaxLength(512);
            // DigiDollar support. Currency defaults to "DGB" so existing rows
            // stay valid; AmountCentsDd is nullable and only populated for DD
            // sessions.
            e.Property(s => s.Currency).HasMaxLength(8).IsRequired().HasDefaultValue("DGB");
            e.Property(s => s.Simulated).HasDefaultValue(false);
            e.Property(s => s.BuyerChoice).HasDefaultValue(false);
            e.Property(s => s.ExtraCurrency).HasMaxLength(8);
            e.Property(s => s.ExtraTxid).HasMaxLength(128);
        });

        // specs/033: one row per currency a buyer-choice session can be paid in.
        modelBuilder.Entity<PaySessionOption>(e =>
        {
            e.ToTable("PaySessionOptions");
            e.HasKey(o => new { o.SessionId, o.Currency });
            e.Property(o => o.SessionId).HasMaxLength(32).IsRequired();
            e.Property(o => o.Currency).HasMaxLength(8).IsRequired();
            e.Property(o => o.Address).HasMaxLength(120).IsRequired();
            e.Property(o => o.UsdRate).HasPrecision(28, 10);
            e.HasIndex(o => o.Address);
            e.HasOne<PaySession>().WithMany().HasForeignKey(o => o.SessionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MerchantSession>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => s.TokenPrefix).IsUnique();
            e.HasIndex(s => s.MerchantId);
            e.Property(s => s.MerchantId).HasMaxLength(32).IsRequired();
            e.Property(s => s.TokenPrefix).HasMaxLength(32).IsRequired();
            e.Property(s => s.TokenHash).HasMaxLength(128).IsRequired();
        });

        modelBuilder.Entity<PayApiKey>(e =>
        {
            e.HasKey(k => k.Id);
            e.HasIndex(k => k.Prefix).IsUnique();
            e.HasIndex(k => k.MerchantId);
            e.Property(k => k.MerchantId).HasMaxLength(32).IsRequired();
            e.Property(k => k.Prefix).HasMaxLength(32).IsRequired();
            e.Property(k => k.Hash).HasMaxLength(128).IsRequired();
            e.Property(k => k.Label).HasMaxLength(80);
            e.Property(k => k.Kind).HasMaxLength(16).IsRequired().HasDefaultValue(ApiKeyKind.Merchant);
            e.Property(k => k.StoreId).HasMaxLength(32);
        });

        modelBuilder.Entity<WebhookDelivery>(e =>
        {
            e.HasKey(d => d.Id);
            e.HasIndex(d => d.StoreId);
            e.HasIndex(d => new { d.StoreId, d.CreatedAt });
            e.Ignore(d => d.Success); // computed from StatusCode
            e.Property(d => d.StoreId).HasMaxLength(32).IsRequired();
            e.Property(d => d.SessionId).HasMaxLength(32);
            e.Property(d => d.EventName).HasMaxLength(60).IsRequired();
            e.Property(d => d.Url).HasMaxLength(500).IsRequired();
            e.Property(d => d.ResponseSnippet).HasMaxLength(2048);
            e.Property(d => d.ErrorMessage).HasMaxLength(500);
            e.Property(d => d.PayoutId).HasMaxLength(32);
        });

        modelBuilder.Entity<AuditEvent>(e =>
        {
            e.HasKey(a => a.Id);
            e.HasIndex(a => new { a.MerchantId, a.CreatedAt });
            e.HasIndex(a => new { a.MerchantId, a.TargetType, a.TargetId });
            e.Property(a => a.MerchantId).HasMaxLength(32).IsRequired();
            e.Property(a => a.ActorType).HasMaxLength(16).IsRequired();
            e.Property(a => a.ActorId).HasMaxLength(80);
            e.Property(a => a.ActorIp).HasMaxLength(64);
            e.Property(a => a.Action).HasMaxLength(60).IsRequired();
            e.Property(a => a.TargetType).HasMaxLength(20).IsRequired();
            e.Property(a => a.TargetId).HasMaxLength(64).IsRequired();
            e.Property(a => a.Summary).HasMaxLength(512);
            e.Property(a => a.Metadata).HasMaxLength(2048);
        });

        modelBuilder.Entity<IdempotencyRecord>(e =>
        {
            e.HasKey(r => r.Id);
            // Scope: same key from two different merchants must not collide,
            // so the uniqueness constraint is composite. The CreatedAt index
            // serves the 24h sweep that prunes stale records.
            e.HasIndex(r => new { r.MerchantId, r.Key }).IsUnique();
            e.HasIndex(r => r.CreatedAt);
            e.Property(r => r.MerchantId).HasMaxLength(32).IsRequired();
            e.Property(r => r.Key).HasMaxLength(255).IsRequired();
            e.Property(r => r.SessionId).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<PayPosPairing>(e =>
        {
            e.HasKey(p => p.Id);
            // Unique: a freshly generated code that collides with a live one is rerolled.
            e.HasIndex(p => p.CodeHash).IsUnique();
            e.HasIndex(p => p.ExpiresAt);
            e.Property(p => p.Id).HasMaxLength(32);
            e.Property(p => p.CodeHash).HasMaxLength(64).IsRequired();
            e.Property(p => p.MerchantId).HasMaxLength(32).IsRequired();
            e.Property(p => p.StoreId).HasMaxLength(32).IsRequired();
            e.Property(p => p.StoreName).HasMaxLength(120).IsRequired();
            e.Property(p => p.MerchantName).HasMaxLength(120).IsRequired();
            e.Property(p => p.DeviceLabel).HasMaxLength(80).IsRequired();
            e.Property(p => p.KeyPrefix).HasMaxLength(32).IsRequired();
        });
    }
}

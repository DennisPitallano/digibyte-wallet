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
        });
    }
}

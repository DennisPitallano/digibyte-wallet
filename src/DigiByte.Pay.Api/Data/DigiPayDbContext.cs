using Microsoft.EntityFrameworkCore;

namespace DigiByte.Pay.Api.Data;

public class DigiPayDbContext : DbContext
{
    public DigiPayDbContext(DbContextOptions<DigiPayDbContext> options) : base(options) { }

    public DbSet<PayMerchant> Merchants => Set<PayMerchant>();
    public DbSet<PaySession> Sessions => Set<PaySession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PayMerchant>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasIndex(m => m.ApiKeyPrefix).IsUnique();
            e.Property(m => m.DisplayName).HasMaxLength(120).IsRequired();
            e.Property(m => m.Xpub).HasMaxLength(200);
            e.Property(m => m.ReceiveAddress).HasMaxLength(80);
            e.Property(m => m.Network).HasMaxLength(20).IsRequired();
            e.Property(m => m.ApiKeyPrefix).HasMaxLength(32).IsRequired();
            e.Property(m => m.ApiKeyHash).HasMaxLength(128).IsRequired();
        });

        modelBuilder.Entity<PaySession>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => s.MerchantId);
            e.HasIndex(s => s.Status);
            e.HasIndex(s => s.Address);
            e.Property(s => s.Address).HasMaxLength(80).IsRequired();
            e.Property(s => s.Label).HasMaxLength(200);
            e.Property(s => s.Memo).HasMaxLength(200);
            e.Property(s => s.FiatCurrency).HasMaxLength(10);
            e.Property(s => s.PaidTxid).HasMaxLength(80);
        });
    }
}

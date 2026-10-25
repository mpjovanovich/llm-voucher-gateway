using LlmVoucherGateway.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LlmVoucherGateway.Api.Persistence;

internal sealed class VoucherDbContext(DbContextOptions<VoucherDbContext> options)
    : DbContext(options)
{
    public DbSet<Voucher> Vouchers => Set<Voucher>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        EntityTypeBuilder<Voucher> voucher = modelBuilder.Entity<Voucher>();

        voucher.HasKey(issuedVoucher => issuedVoucher.Id);

        // Owned, so both value objects live in the voucher's own table as Key_Hash,
        // Key_Prefix, Lifetime_CreatedAt and Lifetime_ExpiresAt rather than becoming
        // rows of their own that could exist without a voucher.
        voucher.OwnsOne(issuedVoucher => issuedVoucher.Key, key =>
        {
            // Milestone 6 resolves a bearer token by hashing it and looking up this
            // column, so the index is what that lookup rides on, and unique is a
            // statement that two vouchers cannot share a secret.
            key.Property(voucherKey => voucherKey.Hash).HasMaxLength(64).IsRequired();
            key.HasIndex(voucherKey => voucherKey.Hash).IsUnique();

            key.Property(voucherKey => voucherKey.Prefix).HasMaxLength(8).IsRequired();
        });

        voucher.OwnsOne(issuedVoucher => issuedVoucher.Lifetime, lifetime =>
        {
            lifetime.Property(voucherLifetime => voucherLifetime.CreatedAt).IsRequired();
            lifetime.Property(voucherLifetime => voucherLifetime.ExpiresAt).IsRequired();
        });

        // Stored as "Active" rather than 0. Readable in a hand-written query, and it
        // cannot be silently reinterpreted if a value is ever inserted into the enum.
        voucher.Property(issuedVoucher => issuedVoucher.Status).HasConversion<string>();
    }
}

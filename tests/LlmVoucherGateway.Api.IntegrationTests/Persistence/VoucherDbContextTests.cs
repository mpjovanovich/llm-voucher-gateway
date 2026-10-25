using LlmVoucherGateway.Api.Persistence;
using LlmVoucherGateway.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace LlmVoucherGateway.Api.IntegrationTests.Persistence;

/// <summary>
/// EF builds and validates its model on first use, so a mapping mistake would otherwise
/// surface at the first query rather than here.
/// </summary>
public class VoucherDbContextTests
{
    [Fact]
    public void Model_MapsBothValueObjectsAsOwnedTypes()
    {
        using GatewayApplicationFactory factory = new();
        using IServiceScope scope = factory.Services.CreateScope();
        VoucherDbContext context = scope.ServiceProvider.GetRequiredService<VoucherDbContext>();

        IEntityType voucher = context.Model.FindEntityType(typeof(Voucher))!;

        Assert.True(voucher.FindNavigation(nameof(Voucher.Key))!.TargetEntityType.IsOwned());
        Assert.True(voucher.FindNavigation(nameof(Voucher.Lifetime))!.TargetEntityType.IsOwned());
    }

    [Fact]
    public void Model_IndexesTheKeyHashUniquely()
    {
        using GatewayApplicationFactory factory = new();
        using IServiceScope scope = factory.Services.CreateScope();
        VoucherDbContext context = scope.ServiceProvider.GetRequiredService<VoucherDbContext>();

        IEntityType voucherKey = context.Model
            .FindEntityType(typeof(Voucher))!
            .FindNavigation(nameof(Voucher.Key))!
            .TargetEntityType;

        IIndex hashIndex = Assert.Single(voucherKey.GetIndexes());

        Assert.Equal(nameof(VoucherKey.Hash), Assert.Single(hashIndex.Properties).Name);
        Assert.True(hashIndex.IsUnique);
    }
}

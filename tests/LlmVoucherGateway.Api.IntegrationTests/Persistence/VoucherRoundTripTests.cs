using LlmVoucherGateway.Api.Persistence;
using LlmVoucherGateway.Domain.Abstractions;
using LlmVoucherGateway.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LlmVoucherGateway.Api.IntegrationTests.Persistence;

/// <summary>
/// Reading is what proves EF can materialize the aggregate: it constructs a
/// <see cref="Voucher"/> through the private parameterless constructor and then fills
/// the owned value objects. The mapping tests only prove the model builds.
/// </summary>
public class VoucherRoundTripTests
{
    [Fact]
    public async Task Save_ThenReloadInANewContext_PreservesTheAggregate()
    {
        await using GatewayApplicationFactory factory = new();

        IssuedVoucher issued;

        using (IServiceScope writeScope = factory.Services.CreateScope())
        {
            VoucherDbContext writeContext = writeScope.ServiceProvider
                .GetRequiredService<VoucherDbContext>();

            issued = Voucher.Issue(
                "CSCI-101",
                allowance: 100_000,
                DateTimeOffset.UtcNow.AddDays(30),
                writeScope.ServiceProvider.GetRequiredService<IDateTimeProvider>()
            );

            writeContext.Vouchers.Add(issued.Voucher);
            await writeContext.SaveChangesAsync();
        }

        // A second scope means a second context with an empty change tracker, so the
        // assertions below run against what SQLite returned rather than the instance
        // that was saved.
        using IServiceScope readScope = factory.Services.CreateScope();
        VoucherDbContext readContext = readScope.ServiceProvider
            .GetRequiredService<VoucherDbContext>();

        Voucher? reloaded = await readContext.Vouchers
            .SingleOrDefaultAsync(voucher => voucher.Id == issued.Voucher.Id);

        Assert.NotNull(reloaded);
        Assert.Equal(issued.Voucher.Key.Hash, reloaded.Key.Hash);
        Assert.Equal(issued.Voucher.Key.Prefix, reloaded.Key.Prefix);
        Assert.Equal(issued.Voucher.Section, reloaded.Section);
        Assert.Equal(issued.Voucher.Allowance, reloaded.Allowance);
        Assert.Equal(0, reloaded.Consumed);
        Assert.Equal(VoucherStatus.Active, reloaded.Status);
        Assert.Equal(issued.Voucher.Lifetime.CreatedAt, reloaded.Lifetime.CreatedAt);
        Assert.Equal(issued.Voucher.Lifetime.ExpiresAt, reloaded.Lifetime.ExpiresAt);
    }
}

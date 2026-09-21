using Microsoft.EntityFrameworkCore;
using SugarERP.Domain;
using SugarERP.Infrastructure.Local;

namespace SugarERP.Branch1.Tests;

internal static class BranchTestFixtureSeeder
{
    public static async Task SeedAsync(LocalDatabase database)
    {
        await using var db = database.CreateContext();
        var now = DateTimeOffset.UtcNow;
        db.DeviceConfigurations.Add(new DeviceConfiguration
        {
            SiteId = Guid.Parse("20000000-0000-4000-8000-000000000001"),
            DeviceId = Guid.Parse("30000000-0000-4000-8000-000000000001"),
            Profile = DeviceProfile.BranchType1,
            SiteName = "Test Branch",
            ApiBaseUrl = "https://erp.example/api/v1",
            DeviceCredential = "test-fixture-credential",
            TouchMode = false,
            EnrolledAtUtc = now
        });
        db.SequenceStates.Add(new SequenceState());

        var items = new[]
        {
            (Guid.Parse("10000000-0000-4000-8000-000000000001"), "CAKE-CHOC", "تورتة شوكولاتة", 45000L, 18L),
            (Guid.Parse("10000000-0000-4000-8000-000000000002"), "GATEAUX-CHOCO", "جاتوه شوكولاتة", 8500L, 46L),
            (Guid.Parse("10000000-0000-4000-8000-000000000003"), "CUPCAKE-PINK", "كب كيك فراولة", 6500L, 32L),
            (Guid.Parse("10000000-0000-4000-8000-000000000004"), "CHEESECAKE", "تشيز كيك", 12000L, 15L),
            (Guid.Parse("10000000-0000-4000-8000-000000000005"), "COOKIES-BOX", "علبة كوكيز", 9500L, 24L),
            (Guid.Parse("10000000-0000-4000-8000-000000000006"), "CROISSANT", "كرواسون", 5500L, 28L)
        };
        foreach (var (id, sku, name, price, quantity) in items)
        {
            db.CatalogItems.Add(new CatalogItem { Id = id, Sku = sku, NameAr = name, Unit = "قطعة", QuantityScale = 1, RetailPriceMinor = price, UpdatedAtUtc = now });
            db.StockBalances.Add(new StockBalance { ItemId = id, QuantityScaled = quantity, Revision = 1, AsOfUtc = now });
            db.StockMovements.Add(new StockMovement { Id = Guid.NewGuid(), DocumentId = Guid.Empty, SourceLineId = id, ItemId = id, Kind = StockMovementKind.InitialBalance, DeltaScaled = quantity, OccurredAtUtc = now });
        }
        await db.SaveChangesAsync();

        var catalog = await db.CatalogItems.AsNoTracking().ToListAsync();
        var customers = new[]
        {
            (Guid.Parse("41000000-0000-4000-8000-000000000001"), "Test Cafe", "Cafe", "01090000001", "Test Address", 75L),
            (Guid.Parse("41000000-0000-4000-8000-000000000002"), "Test Market", "Market", "01090000002", "Test Address", 70L)
        };
        foreach (var (id, name, kind, phone, address, percentage) in customers)
        {
            var customer = new CafeCustomer
            {
                Id = id,
                CommandId = Guid.NewGuid(),
                Name = name,
                Kind = kind,
                Phone = phone,
                Address = address,
                Active = true,
                Version = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            foreach (var item in catalog)
            {
                customer.Prices.Add(new CafePrice
                {
                    Id = Guid.NewGuid(),
                    CafeCustomerId = customer.Id,
                    ItemId = item.Id,
                    UnitPriceMinor = item.RetailPriceMinor * percentage / 100,
                    UpdatedAtUtc = now
                });
            }
            db.CafeCustomers.Add(customer);
        }
        await db.SaveChangesAsync();
    }
}

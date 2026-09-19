using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SugarERP.Infrastructure.Local;

public sealed class BranchDbContextFactory : IDesignTimeDbContextFactory<BranchDbContext>
{
    public BranchDbContext CreateDbContext(string[] args)
    {
        var designPath = Path.Combine(Path.GetTempPath(), "sugar-erp-branch-type-1-design.db");
        var options = new DbContextOptionsBuilder<BranchDbContext>()
            .UseSqlite($"Data Source={designPath}")
            .Options;
        return new BranchDbContext(options);
    }
}

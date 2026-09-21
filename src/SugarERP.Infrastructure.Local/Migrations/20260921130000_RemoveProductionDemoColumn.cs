using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SugarERP.Infrastructure.Local.Migrations;

[DbContext(typeof(BranchDbContext))]
[Migration("20260921130000_RemoveProductionDemoColumn")]
public sealed class RemoveProductionDemoColumn : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE shipments DROP COLUMN SyntheticDemo;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE shipments ADD COLUMN SyntheticDemo INTEGER NOT NULL DEFAULT 0;");
    }
}

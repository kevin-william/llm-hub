using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LlmHub.Infrastructure.Persistence.Migrations;

[DbContext(typeof(HubDbContext))]
[Migration("20261002120000_AddTenantBoundaries")]
public partial class AddTenantBoundaries : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "TenantId", table: "channels", type: "character varying(160)", maxLength: 160, nullable: false, defaultValue: "tenant:development");
        migrationBuilder.AddColumn<string>(name: "TenantId", table: "endpoints", type: "character varying(160)", maxLength: 160, nullable: false, defaultValue: "tenant:development");
        migrationBuilder.AddColumn<string>(name: "TenantId", table: "workers", type: "character varying(160)", maxLength: 160, nullable: false, defaultValue: "tenant:development");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "TenantId", table: "channels");
        migrationBuilder.DropColumn(name: "TenantId", table: "endpoints");
        migrationBuilder.DropColumn(name: "TenantId", table: "workers");
    }
}

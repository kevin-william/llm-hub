using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LlmHub.Infrastructure.Persistence.Migrations;

[DbContext(typeof(HubDbContext))]
[Migration("20261002110000_PersistChannelDestination")]
public partial class PersistChannelDestination : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Destination",
            table: "channels",
            type: "character varying(256)",
            maxLength: 256,
            nullable: false,
            defaultValue: "agent:opencode/default");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Destination",
            table: "channels");
    }
}

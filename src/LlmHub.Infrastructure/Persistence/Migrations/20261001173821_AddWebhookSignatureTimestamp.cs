using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LlmHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookSignatureTimestamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SignatureTimestamp",
                table: "webhook_deliveries",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SignatureTimestamp",
                table: "webhook_deliveries");
        }
    }
}

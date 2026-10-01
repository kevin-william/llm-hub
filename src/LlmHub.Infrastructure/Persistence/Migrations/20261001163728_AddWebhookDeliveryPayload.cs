using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LlmHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookDeliveryPayload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CallbackUrl",
                table: "webhook_deliveries",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "webhook_deliveries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                table: "webhook_deliveries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Payload",
                table: "webhook_deliveries",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CallbackUrl",
                table: "webhook_deliveries");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "webhook_deliveries");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "webhook_deliveries");

            migrationBuilder.DropColumn(
                name: "Payload",
                table: "webhook_deliveries");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LlmHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LinkArtifactsToRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_artifacts_ContentHash",
                table: "artifacts");

            migrationBuilder.AddColumn<string>(
                name: "ChannelId",
                table: "artifacts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RunId",
                table: "artifacts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_artifacts_RunId_ContentHash",
                table: "artifacts",
                columns: new[] { "RunId", "ContentHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_artifacts_RunId_ContentHash",
                table: "artifacts");

            migrationBuilder.DropColumn(
                name: "ChannelId",
                table: "artifacts");

            migrationBuilder.DropColumn(
                name: "RunId",
                table: "artifacts");

            migrationBuilder.CreateIndex(
                name: "IX_artifacts_ContentHash",
                table: "artifacts",
                column: "ContentHash",
                unique: true);
        }
    }
}

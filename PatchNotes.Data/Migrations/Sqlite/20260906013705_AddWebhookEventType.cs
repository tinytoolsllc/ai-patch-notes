using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchNotes.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddWebhookEventType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EventType",
                table: "ProcessedWebhookEvents",
                type: "TEXT",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EventType",
                table: "ProcessedWebhookEvents");
        }
    }
}

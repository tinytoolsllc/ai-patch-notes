using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchNotes.Data.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class AddSentDigestEmails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SentDigestEmails",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(21)", maxLength: 21, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(21)", maxLength: 21, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    HtmlBody = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecipientEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ResendEmailId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentDigestEmails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SentDigestEmails_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SentDigestEmails_SentAt",
                table: "SentDigestEmails",
                column: "SentAt");

            migrationBuilder.CreateIndex(
                name: "IX_SentDigestEmails_UserId",
                table: "SentDigestEmails",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SentDigestEmails");
        }
    }
}

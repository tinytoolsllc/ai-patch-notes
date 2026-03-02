using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchNotes.Data.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class AddWatchlistTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WatchlistTemplates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(21)", maxLength: 21, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchlistTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WatchlistTemplatePackages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(21)", maxLength: 21, nullable: false),
                    WatchlistTemplateId = table.Column<string>(type: "nvarchar(21)", maxLength: 21, nullable: false),
                    PackageId = table.Column<string>(type: "nvarchar(21)", maxLength: 21, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchlistTemplatePackages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WatchlistTemplatePackages_Packages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "Packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WatchlistTemplatePackages_WatchlistTemplates_WatchlistTemplateId",
                        column: x => x.WatchlistTemplateId,
                        principalTable: "WatchlistTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WatchlistTemplatePackages_PackageId",
                table: "WatchlistTemplatePackages",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_WatchlistTemplatePackages_WatchlistTemplateId_PackageId",
                table: "WatchlistTemplatePackages",
                columns: new[] { "WatchlistTemplateId", "PackageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WatchlistTemplates_Name",
                table: "WatchlistTemplates",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WatchlistTemplatePackages");

            migrationBuilder.DropTable(
                name: "WatchlistTemplates");
        }
    }
}

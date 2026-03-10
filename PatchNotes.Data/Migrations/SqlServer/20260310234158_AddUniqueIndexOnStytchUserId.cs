using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchNotes.Data.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class AddUniqueIndexOnStytchUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: Merge duplicate users (keep the row that has watchlist data or the earliest one)
            // For each StytchUserId with multiple rows, keep the one with the most watchlist entries
            // (or the earliest-created if tied), and reassign all related data to it.
            migrationBuilder.Sql("""
                -- Identify the "keeper" user for each StytchUserId (most watchlist entries, then earliest created)
                WITH RankedUsers AS (
                    SELECT
                        u.Id,
                        u.StytchUserId,
                        ROW_NUMBER() OVER (
                            PARTITION BY u.StytchUserId
                            ORDER BY
                                (SELECT COUNT(*) FROM Watchlists w WHERE w.UserId = u.Id) DESC,
                                u.CreatedAt ASC
                        ) AS rn
                    FROM Users u
                ),
                Keepers AS (
                    SELECT Id, StytchUserId FROM RankedUsers WHERE rn = 1
                ),
                Losers AS (
                    SELECT r.Id, r.StytchUserId FROM RankedUsers r WHERE r.rn > 1
                )
                -- Reassign watchlists from duplicate users to the keeper
                UPDATE w
                SET w.UserId = k.Id
                FROM Watchlists w
                INNER JOIN Losers l ON w.UserId = l.Id
                INNER JOIN Keepers k ON l.StytchUserId = k.StytchUserId
                WHERE NOT EXISTS (
                    -- Skip if the keeper already watches this package (avoid unique constraint violation)
                    SELECT 1 FROM Watchlists w2 WHERE w2.UserId = k.Id AND w2.PackageId = w.PackageId
                );

                -- Reassign SentDigestEmails from duplicates to keeper
                WITH RankedUsers AS (
                    SELECT
                        u.Id,
                        u.StytchUserId,
                        ROW_NUMBER() OVER (
                            PARTITION BY u.StytchUserId
                            ORDER BY
                                (SELECT COUNT(*) FROM Watchlists w WHERE w.UserId = u.Id) DESC,
                                u.CreatedAt ASC
                        ) AS rn
                    FROM Users u
                ),
                Keepers AS (
                    SELECT Id, StytchUserId FROM RankedUsers WHERE rn = 1
                ),
                Losers AS (
                    SELECT r.Id, r.StytchUserId FROM RankedUsers r WHERE r.rn > 1
                )
                UPDATE sde
                SET sde.UserId = k.Id
                FROM SentDigestEmails sde
                INNER JOIN Losers l ON sde.UserId = l.Id
                INNER JOIN Keepers k ON l.StytchUserId = k.StytchUserId;

                -- Delete orphaned watchlists on duplicate users (ones that already existed on keeper)
                WITH RankedUsers AS (
                    SELECT
                        u.Id,
                        u.StytchUserId,
                        ROW_NUMBER() OVER (
                            PARTITION BY u.StytchUserId
                            ORDER BY
                                (SELECT COUNT(*) FROM Watchlists w WHERE w.UserId = u.Id) DESC,
                                u.CreatedAt ASC
                        ) AS rn
                    FROM Users u
                ),
                Losers AS (
                    SELECT r.Id FROM RankedUsers r WHERE r.rn > 1
                )
                DELETE w FROM Watchlists w
                INNER JOIN Losers l ON w.UserId = l.Id;

                -- Delete the duplicate user rows
                WITH RankedUsers AS (
                    SELECT
                        u.Id,
                        u.StytchUserId,
                        ROW_NUMBER() OVER (
                            PARTITION BY u.StytchUserId
                            ORDER BY
                                (SELECT COUNT(*) FROM Watchlists w WHERE w.UserId = u.Id) DESC,
                                u.CreatedAt ASC
                        ) AS rn
                    FROM Users u
                ),
                Losers AS (
                    SELECT r.Id FROM RankedUsers r WHERE r.rn > 1
                )
                DELETE u FROM Users u
                INNER JOIN Losers l ON u.Id = l.Id;
                """);

            // Step 2: Drop existing non-unique index if it exists
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Users_StytchUserId' AND object_id = OBJECT_ID('Users'))
                    DROP INDEX IX_Users_StytchUserId ON Users;
                """);

            // Step 3: Create the unique index
            migrationBuilder.CreateIndex(
                name: "IX_Users_StytchUserId",
                table: "Users",
                column: "StytchUserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_StytchUserId",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Users_StytchUserId",
                table: "Users",
                column: "StytchUserId");
        }
    }
}

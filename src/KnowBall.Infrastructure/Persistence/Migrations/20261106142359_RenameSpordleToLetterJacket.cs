using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowBall.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The word game was renamed from Spordle to Letter Jacket. Rename its table, key and index in place so
    /// rounds in progress survive the deploy.
    /// </summary>
    public partial class RenameSpordleToLetterJacket : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(name: "SpordleSessions", newName: "LetterJacketSessions");
            migrationBuilder.Sql("""ALTER TABLE "LetterJacketSessions" RENAME CONSTRAINT "PK_SpordleSessions" TO "PK_LetterJacketSessions";""");
            migrationBuilder.RenameIndex(
                name: "IX_SpordleSessions_ExpiresAtUtc",
                table: "LetterJacketSessions",
                newName: "IX_LetterJacketSessions_ExpiresAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_LetterJacketSessions_ExpiresAtUtc",
                table: "LetterJacketSessions",
                newName: "IX_SpordleSessions_ExpiresAtUtc");
            migrationBuilder.Sql("""ALTER TABLE "LetterJacketSessions" RENAME CONSTRAINT "PK_LetterJacketSessions" TO "PK_SpordleSessions";""");
            migrationBuilder.RenameTable(name: "LetterJacketSessions", newName: "SpordleSessions");
        }
    }
}

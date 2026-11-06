using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowBall.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistLetterJacketSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LetterJacketSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Answer = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    Category = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    SolvedPositions = table.Column<int>(type: "integer", nullable: false),
                    LetterHintPosition = table.Column<int>(type: "integer", nullable: true),
                    Complete = table.Column<bool>(type: "boolean", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    GuessesJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LetterJacketSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LetterJacketSessions_ExpiresAtUtc",
                table: "LetterJacketSessions",
                column: "ExpiresAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LetterJacketSessions");
        }
    }
}

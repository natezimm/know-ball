using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowBall.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistNumbersBetaPlay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NumbersPlays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Owner = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StartKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Filters = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SecretJson = table.Column<string>(type: "text", nullable: false),
                    AnswersJson = table.Column<string>(type: "text", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NumbersPlays", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NumbersPlays_ExpiresAt",
                table: "NumbersPlays",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_NumbersPlays_Owner_StartKey",
                table: "NumbersPlays",
                columns: new[] { "Owner", "StartKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NumbersPlays");
        }
    }
}

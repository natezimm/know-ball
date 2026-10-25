using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowBall.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistNumbersContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NumbersCatalogues",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentJson = table.Column<string>(type: "text", nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NumbersCatalogues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NumbersQuizzes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    CatalogueId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    QuestionsJson = table.Column<string>(type: "text", nullable: false),
                    AlgorithmVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NumbersQuizzes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NumbersQuizzes_NumbersCatalogues_CatalogueId",
                        column: x => x.CatalogueId,
                        principalTable: "NumbersCatalogues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NumbersQuizzes_CatalogueId",
                table: "NumbersQuizzes",
                column: "CatalogueId");

            migrationBuilder.CreateIndex(
                name: "IX_NumbersQuizzes_Date",
                table: "NumbersQuizzes",
                column: "Date",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NumbersQuizzes");

            migrationBuilder.DropTable(
                name: "NumbersCatalogues");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace KnowBall.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VersionOne : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Defaults below were set by hand for rows that already exist: an old puzzle is a scheduled
            // one with an empty secret, and an old season line is the player's whole season.
            migrationBuilder.DropIndex(
                name: "IX_PlayerSeasonStats_PlayerId_Season",
                table: "PlayerSeasonStats");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CompletedAt",
                table: "UserResults",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<int>(
                name: "HintsUsed",
                table: "UserResults",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartedAt",
                table: "UserResults",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<int>(
                name: "ActiveFrom",
                table: "Teams",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ActiveTo",
                table: "Teams",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FranchiseId",
                table: "Teams",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "Date",
                table: "Puzzles",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Puzzles",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<int>(
                name: "Number",
                table: "Puzzles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecretJson",
                table: "Puzzles",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Puzzles",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Scheduled");

            migrationBuilder.AddColumn<int>(
                name: "Seasons",
                table: "PlayerTeamStints",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Games",
                table: "PlayerSeasonStats",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSeasonTotal",
                table: "PlayerSeasonStats",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<double>(
                name: "MinutesPerGame",
                table: "PlayerSeasonStats",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "BirthLatitude",
                table: "Players",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "BirthLongitude",
                table: "Players",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BirthPlace",
                table: "Players",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsHallOfFame",
                table: "Players",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ToYear",
                table: "Players",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlayerExternalIds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlayerId = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Value = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerExternalIds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerExternalIds_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Teams_FranchiseId",
                table: "Teams",
                column: "FranchiseId");

            migrationBuilder.CreateIndex(
                name: "IX_Puzzles_GameType_Status",
                table: "Puzzles",
                columns: new[] { "GameType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSeasonStats_PlayerId_Season_TeamId",
                table: "PlayerSeasonStats",
                columns: new[] { "PlayerId", "Season", "TeamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerExternalIds_PlayerId_Source",
                table: "PlayerExternalIds",
                columns: new[] { "PlayerId", "Source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerExternalIds_Source_Value",
                table: "PlayerExternalIds",
                columns: new[] { "Source", "Value" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Teams_Teams_FranchiseId",
                table: "Teams",
                column: "FranchiseId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Teams_Teams_FranchiseId",
                table: "Teams");

            migrationBuilder.DropTable(
                name: "PlayerExternalIds");

            migrationBuilder.DropIndex(
                name: "IX_Teams_FranchiseId",
                table: "Teams");

            migrationBuilder.DropIndex(
                name: "IX_Puzzles_GameType_Status",
                table: "Puzzles");

            migrationBuilder.DropIndex(
                name: "IX_PlayerSeasonStats_PlayerId_Season_TeamId",
                table: "PlayerSeasonStats");

            migrationBuilder.DropColumn(
                name: "HintsUsed",
                table: "UserResults");

            migrationBuilder.DropColumn(
                name: "StartedAt",
                table: "UserResults");

            migrationBuilder.DropColumn(
                name: "ActiveFrom",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "ActiveTo",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "FranchiseId",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Puzzles");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "Puzzles");

            migrationBuilder.DropColumn(
                name: "SecretJson",
                table: "Puzzles");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Puzzles");

            migrationBuilder.DropColumn(
                name: "Seasons",
                table: "PlayerTeamStints");

            migrationBuilder.DropColumn(
                name: "Games",
                table: "PlayerSeasonStats");

            migrationBuilder.DropColumn(
                name: "IsSeasonTotal",
                table: "PlayerSeasonStats");

            migrationBuilder.DropColumn(
                name: "MinutesPerGame",
                table: "PlayerSeasonStats");

            migrationBuilder.DropColumn(
                name: "BirthLatitude",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "BirthLongitude",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "BirthPlace",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "IsHallOfFame",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "ToYear",
                table: "Players");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CompletedAt",
                table: "UserResults",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "Date",
                table: "Puzzles",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSeasonStats_PlayerId_Season",
                table: "PlayerSeasonStats",
                columns: new[] { "PlayerId", "Season" },
                unique: true);
        }
    }
}

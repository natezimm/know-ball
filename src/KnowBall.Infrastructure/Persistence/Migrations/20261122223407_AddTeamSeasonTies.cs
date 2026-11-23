using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowBall.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddTeamSeasonTies : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "Ties",
            table: "TeamSeasons",
            type: "integer",
            nullable: false,
            defaultValue: 0);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Ties",
            table: "TeamSeasons");
    }
}

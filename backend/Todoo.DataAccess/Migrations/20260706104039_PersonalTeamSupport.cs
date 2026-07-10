using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Todoo.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class PersonalTeamSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPersonal",
                table: "Teams",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPersonal",
                table: "Teams");
        }
    }
}

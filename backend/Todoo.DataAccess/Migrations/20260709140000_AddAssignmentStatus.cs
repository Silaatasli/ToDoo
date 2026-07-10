using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Todoo.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignmentStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AssignmentStatus",
                table: "TaskItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                UPDATE TaskItems
                SET AssignmentStatus = 2
                WHERE AssignedToUserId IS NOT NULL
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssignmentStatus",
                table: "TaskItems");
        }
    }
}

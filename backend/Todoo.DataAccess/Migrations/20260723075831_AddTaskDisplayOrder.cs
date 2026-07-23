using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Todoo.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskDisplayOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DisplayOrder",
                table: "TaskItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                ;WITH OrderedTasks AS (
                    SELECT
                        Id,
                        ROW_NUMBER() OVER (
                            PARTITION BY BoardColumnId
                            ORDER BY Id
                        ) - 1 AS NewOrder
                    FROM TaskItems
                    WHERE DeletedAt IS NULL
                )
                UPDATE t
                SET DisplayOrder = o.NewOrder
                FROM TaskItems t
                INNER JOIN OrderedTasks o ON t.Id = o.Id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayOrder",
                table: "TaskItems");
        }
    }
}

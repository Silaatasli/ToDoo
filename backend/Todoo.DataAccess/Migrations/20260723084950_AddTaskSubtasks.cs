using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Todoo.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskSubtasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ParentTaskId",
                table: "TaskItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubtaskStatus",
                table: "TaskItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_ParentTaskId",
                table: "TaskItems",
                column: "ParentTaskId");

            migrationBuilder.AddForeignKey(
                name: "FK_TaskItems_TaskItems_ParentTaskId",
                table: "TaskItems",
                column: "ParentTaskId",
                principalTable: "TaskItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskItems_TaskItems_ParentTaskId",
                table: "TaskItems");

            migrationBuilder.DropIndex(
                name: "IX_TaskItems_ParentTaskId",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "ParentTaskId",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "SubtaskStatus",
                table: "TaskItems");
        }
    }
}

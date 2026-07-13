using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Todoo.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class TaskActivityLogTaskIdNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskActivityLogs_TaskItems_TaskId",
                table: "TaskActivityLogs");

            migrationBuilder.AlterColumn<int>(
                name: "TaskId",
                table: "TaskActivityLogs",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            // SQL Server multiple cascade path kisitlamasi nedeniyle SetNull kullanilamaz;
            // gorev silinmeden once TaskId uygulama katmaninda null'lanir.
            migrationBuilder.AddForeignKey(
                name: "FK_TaskActivityLogs_TaskItems_TaskId",
                table: "TaskActivityLogs",
                column: "TaskId",
                principalTable: "TaskItems",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskActivityLogs_TaskItems_TaskId",
                table: "TaskActivityLogs");

            migrationBuilder.Sql(
                """
                DELETE FROM TaskActivityLogs WHERE TaskId IS NULL;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "TaskId",
                table: "TaskActivityLogs",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_TaskActivityLogs_TaskItems_TaskId",
                table: "TaskActivityLogs",
                column: "TaskId",
                principalTable: "TaskItems",
                principalColumn: "Id");
        }
    }
}

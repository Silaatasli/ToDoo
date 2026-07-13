using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Todoo.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddBoards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Boards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TeamId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Boards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Boards_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Boards_TeamId",
                table: "Boards",
                column: "TeamId");

            migrationBuilder.Sql(
                """
                INSERT INTO Boards (TeamId, Name, DisplayOrder, CreatedDate)
                SELECT Id, N'Ana pano', 0, SYSUTCDATETIME()
                FROM Teams;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_TeamBoardColumns_Teams_TeamId",
                table: "TeamBoardColumns");

            migrationBuilder.AddColumn<int>(
                name: "BoardId",
                table: "TeamBoardColumns",
                type: "int",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE c
                SET c.BoardId = b.Id
                FROM TeamBoardColumns c
                INNER JOIN Boards b ON b.TeamId = c.TeamId;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "BoardId",
                table: "TeamBoardColumns",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.DropIndex(
                name: "IX_TeamBoardColumns_TeamId",
                table: "TeamBoardColumns");

            migrationBuilder.DropColumn(
                name: "TeamId",
                table: "TeamBoardColumns");

            migrationBuilder.CreateIndex(
                name: "IX_TeamBoardColumns_BoardId",
                table: "TeamBoardColumns",
                column: "BoardId");

            migrationBuilder.AddForeignKey(
                name: "FK_TeamBoardColumns_Boards_BoardId",
                table: "TeamBoardColumns",
                column: "BoardId",
                principalTable: "Boards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddColumn<int>(
                name: "BoardId",
                table: "TaskItems",
                type: "int",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE t
                SET t.BoardId = c.BoardId
                FROM TaskItems t
                INNER JOIN TeamBoardColumns c ON c.Id = t.BoardColumnId;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "BoardId",
                table: "TaskItems",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_BoardId",
                table: "TaskItems",
                column: "BoardId");

            migrationBuilder.AddForeignKey(
                name: "FK_TaskItems_Boards_BoardId",
                table: "TaskItems",
                column: "BoardId",
                principalTable: "Boards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskItems_Boards_BoardId",
                table: "TaskItems");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamBoardColumns_Boards_BoardId",
                table: "TeamBoardColumns");

            migrationBuilder.DropIndex(
                name: "IX_TaskItems_BoardId",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "BoardId",
                table: "TaskItems");

            migrationBuilder.AddColumn<int>(
                name: "TeamId",
                table: "TeamBoardColumns",
                type: "int",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE c
                SET c.TeamId = b.TeamId
                FROM TeamBoardColumns c
                INNER JOIN Boards b ON b.Id = c.BoardId;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "TeamId",
                table: "TeamBoardColumns",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.DropIndex(
                name: "IX_TeamBoardColumns_BoardId",
                table: "TeamBoardColumns");

            migrationBuilder.DropColumn(
                name: "BoardId",
                table: "TeamBoardColumns");

            migrationBuilder.DropTable(
                name: "Boards");

            migrationBuilder.CreateIndex(
                name: "IX_TeamBoardColumns_TeamId",
                table: "TeamBoardColumns",
                column: "TeamId");

            migrationBuilder.AddForeignKey(
                name: "FK_TeamBoardColumns_Teams_TeamId",
                table: "TeamBoardColumns",
                column: "TeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Todoo.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamAnnouncements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanPublishAnnouncements",
                table: "TeamMembers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "TeamAnnouncements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TeamId = table.Column<int>(type: "int", nullable: false),
                    AuthorUserId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamAnnouncements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamAnnouncements_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamAnnouncements_Users_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeamAnnouncements_AuthorUserId",
                table: "TeamAnnouncements",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamAnnouncements_TeamId_CreatedDate",
                table: "TeamAnnouncements",
                columns: new[] { "TeamId", "CreatedDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TeamAnnouncements");

            migrationBuilder.DropColumn(
                name: "CanPublishAnnouncements",
                table: "TeamMembers");
        }
    }
}

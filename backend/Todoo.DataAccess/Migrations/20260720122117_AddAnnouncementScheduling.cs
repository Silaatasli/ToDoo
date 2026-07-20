using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Todoo.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddAnnouncementScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedAt",
                table: "TeamAnnouncements",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledPublishAt",
                table: "TeamAnnouncements",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "TeamAnnouncements",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                UPDATE TeamAnnouncements
                SET Status = 2,
                    PublishedAt = CreatedDate
                WHERE Status = 0 AND PublishedAt IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PublishedAt",
                table: "TeamAnnouncements");

            migrationBuilder.DropColumn(
                name: "ScheduledPublishAt",
                table: "TeamAnnouncements");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "TeamAnnouncements");
        }
    }
}

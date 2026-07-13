using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Todoo.DataAccess.Contexts;

#nullable disable

namespace Todoo.DataAccess.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(TodooDbContext))]
    [Migration("20260713070000_AddUserProfilePhoto")]
    public partial class AddUserProfilePhoto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProfilePhotoContentType",
                table: "Users",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfilePhotoFileName",
                table: "Users",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfilePhotoObjectKey",
                table: "Users",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ProfilePhotoSizeBytes",
                table: "Users",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProfilePhotoContentType",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ProfilePhotoFileName",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ProfilePhotoObjectKey",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ProfilePhotoSizeBytes",
                table: "Users");
        }
    }
}

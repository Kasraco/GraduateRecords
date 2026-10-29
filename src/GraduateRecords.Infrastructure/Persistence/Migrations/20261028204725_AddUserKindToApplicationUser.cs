using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GraduateRecords.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserKindToApplicationUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UserKind",
                table: "AspNetUsers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Student");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UserKind",
                table: "AspNetUsers");
        }
    }
}

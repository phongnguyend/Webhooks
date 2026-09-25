using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebhookRouter.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPasswordAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowPasswordAuthentication",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowPasswordAuthentication",
                table: "Users");
        }
    }
}

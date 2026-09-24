using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebhookRouter.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceBusQueueDestination : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ServiceBusTopicName",
                table: "Topics",
                newName: "ServiceBusEntityName");

            migrationBuilder.AddColumn<string>(
                name: "ServiceBusEntityType",
                table: "Topics",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Topic");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ServiceBusEntityName",
                table: "Topics",
                newName: "ServiceBusTopicName");

            migrationBuilder.DropColumn(
                name: "ServiceBusEntityType",
                table: "Topics");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebhookRouter.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantTopicAuditUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "Topics",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                table: "Topics",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                table: "Tenants",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Topics_CreatedByUserId",
                table: "Topics",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Topics_UpdatedByUserId",
                table: "Topics",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_UpdatedByUserId",
                table: "Tenants",
                column: "UpdatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tenants_Users_UpdatedByUserId",
                table: "Tenants",
                column: "UpdatedByUserId",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Topics_Users_CreatedByUserId",
                table: "Topics",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Topics_Users_UpdatedByUserId",
                table: "Topics",
                column: "UpdatedByUserId",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tenants_Users_UpdatedByUserId",
                table: "Tenants");

            migrationBuilder.DropForeignKey(
                name: "FK_Topics_Users_CreatedByUserId",
                table: "Topics");

            migrationBuilder.DropForeignKey(
                name: "FK_Topics_Users_UpdatedByUserId",
                table: "Topics");

            migrationBuilder.DropIndex(
                name: "IX_Topics_CreatedByUserId",
                table: "Topics");

            migrationBuilder.DropIndex(
                name: "IX_Topics_UpdatedByUserId",
                table: "Topics");

            migrationBuilder.DropIndex(
                name: "IX_Tenants_UpdatedByUserId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Topics");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "Topics");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "Tenants");
        }
    }
}

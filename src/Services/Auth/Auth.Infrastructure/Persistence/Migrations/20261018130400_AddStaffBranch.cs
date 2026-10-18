using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffBranch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_UserId",
                table: "RefreshTokens");

            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "Users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId_RevokedAtUtc",
                table: "RefreshTokens",
                columns: new[] { "UserId", "RevokedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_UserId_RevokedAtUtc",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId",
                table: "RefreshTokens",
                column: "UserId");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Featly.Storage.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleUpgradeRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RoleUpgradeRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TargetProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TargetEnvironmentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RequestedRoleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Justification = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    DecidedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DecisionComment = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    DecidedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleUpgradeRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoleUpgradeRequests_Status",
                table: "RoleUpgradeRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_RoleUpgradeRequests_UserId",
                table: "RoleUpgradeRequests",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoleUpgradeRequests");
        }
    }
}

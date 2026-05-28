using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Featly.Storage.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApprovalPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Required = table.Column<bool>(type: "INTEGER", nullable: false),
                    MinApprovals = table.Column<int>(type: "INTEGER", nullable: false),
                    AuthorCanApproveOwnChange = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowEmergencyBypass = table.Column<bool>(type: "INTEGER", nullable: false),
                    ApproverRules = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PendingChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EntityKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ProposedState = table.Column<string>(type: "TEXT", nullable: false),
                    CurrentState = table.Column<string>(type: "TEXT", nullable: true),
                    AuthorUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AuthorMessage = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    AppliedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AppliedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    RejectedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    RejectionReason = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    WasEmergencyBypass = table.Column<bool>(type: "INTEGER", nullable: false),
                    EmergencyReason = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Approvals = table.Column<string>(type: "TEXT", nullable: true),
                    Comments = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingChanges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalPolicies_EnvironmentId",
                table: "ApprovalPolicies",
                column: "EnvironmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PendingChanges_EnvironmentId",
                table: "PendingChanges",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingChanges_Status",
                table: "PendingChanges",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalPolicies");

            migrationBuilder.DropTable(
                name: "PendingChanges");
        }
    }
}

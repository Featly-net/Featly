using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Featly.Storage.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledApplyAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ScheduledApplyAt",
                table: "PendingChanges",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PendingChanges_Status_ScheduledApplyAt",
                table: "PendingChanges",
                columns: new[] { "Status", "ScheduledApplyAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PendingChanges_Status_ScheduledApplyAt",
                table: "PendingChanges");

            migrationBuilder.DropColumn(
                name: "ScheduledApplyAt",
                table: "PendingChanges");
        }
    }
}

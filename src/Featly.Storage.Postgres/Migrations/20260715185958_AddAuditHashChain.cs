using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Featly.Storage.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditHashChain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Hash",
                table: "AuditEntries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviousHash",
                table: "AuditEntries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Sequence",
                table: "AuditEntries",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_Sequence",
                table: "AuditEntries",
                column: "Sequence");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_Sequence",
                table: "AuditEntries");

            migrationBuilder.DropColumn(
                name: "Hash",
                table: "AuditEntries");

            migrationBuilder.DropColumn(
                name: "PreviousHash",
                table: "AuditEntries");

            migrationBuilder.DropColumn(
                name: "Sequence",
                table: "AuditEntries");
        }
    }
}

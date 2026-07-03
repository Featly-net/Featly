using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Featly.Storage.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddFlagPrerequisites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Prerequisites",
                table: "Flags",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Prerequisites",
                table: "Flags");
        }
    }
}

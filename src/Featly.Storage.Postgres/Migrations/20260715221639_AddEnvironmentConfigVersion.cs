using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Featly.Storage.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddEnvironmentConfigVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ConfigVersion",
                table: "Environments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfigVersion",
                table: "Environments");
        }
    }
}

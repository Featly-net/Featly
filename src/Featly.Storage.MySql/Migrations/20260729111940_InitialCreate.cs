using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Featly.Storage.MySql.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Environments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ProjectId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Key = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsDefault = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ReadOnly = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ConfigVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Environments", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Flags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Key = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Type = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DefaultVariantKey = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Variants = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Rules = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Prerequisites = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EnvironmentId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Tags = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Archived = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdatedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flags", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Key = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsDefault = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Environments_ProjectId_Key",
                table: "Environments",
                columns: new[] { "ProjectId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Flags_EnvironmentId",
                table: "Flags",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Flags_EnvironmentId_Key",
                table: "Flags",
                columns: new[] { "EnvironmentId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Key",
                table: "Projects",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Environments");

            migrationBuilder.DropTable(
                name: "Flags");

            migrationBuilder.DropTable(
                name: "Projects");
        }
    }
}

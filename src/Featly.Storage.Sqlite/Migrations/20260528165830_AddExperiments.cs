using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Featly.Storage.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddExperiments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExperimentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    VariantKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    AssignedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    FlagKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ConfigKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CustomKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SubjectKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    VariantKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Properties = table.Column<string>(type: "TEXT", nullable: true),
                    At = table.Column<long>(type: "INTEGER", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Experiments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Hypothesis = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    FlagKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    MetricKeys = table.Column<string>(type: "TEXT", nullable: false),
                    StickyAssignments = table.Column<bool>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    StoppedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    EnvironmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Experiments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_ExperimentId_SubjectKey",
                table: "Assignments",
                columns: new[] { "ExperimentId", "SubjectKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_EnvironmentId",
                table: "Events",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_EnvironmentId_CustomKey",
                table: "Events",
                columns: new[] { "EnvironmentId", "CustomKey" });

            migrationBuilder.CreateIndex(
                name: "IX_Events_EnvironmentId_FlagKey",
                table: "Events",
                columns: new[] { "EnvironmentId", "FlagKey" });

            migrationBuilder.CreateIndex(
                name: "IX_Events_EnvironmentId_Type",
                table: "Events",
                columns: new[] { "EnvironmentId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_Events_SubjectKey",
                table: "Events",
                column: "SubjectKey");

            migrationBuilder.CreateIndex(
                name: "IX_Experiments_EnvironmentId",
                table: "Experiments",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Experiments_EnvironmentId_Key",
                table: "Experiments",
                columns: new[] { "EnvironmentId", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Assignments");

            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "Experiments");
        }
    }
}

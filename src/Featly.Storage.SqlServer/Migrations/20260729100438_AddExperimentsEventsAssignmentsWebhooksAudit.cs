using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Featly.Storage.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddExperimentsEventsAssignmentsWebhooksAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExperimentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    VariantKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EntityKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorIdentifier = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Data = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    PreviousHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    FlagKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ConfigKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CustomKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SubjectKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    VariantKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Properties = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Experiments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Hypothesis = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    FlagKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MetricKeys = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StickyAssignments = table.Column<bool>(type: "bit", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StoppedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Experiments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WebhookDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WebhookEndpointId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastStatusCode = table.Column<int>(type: "int", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookDeliveries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WebhookEndpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    Secret = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    EventTypes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "int", nullable: false),
                    CircuitOpenUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookEndpoints", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_ExperimentId_SubjectKey",
                table: "Assignments",
                columns: new[] { "ExperimentId", "SubjectKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_At",
                table: "AuditEntries",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_EntityType_EntityKey",
                table: "AuditEntries",
                columns: new[] { "EntityType", "EntityKey" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_EnvironmentId",
                table: "AuditEntries",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_Sequence",
                table: "AuditEntries",
                column: "Sequence");

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

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_Status_NextAttemptAt",
                table: "WebhookDeliveries",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_WebhookEndpointId",
                table: "WebhookDeliveries",
                column: "WebhookEndpointId");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookEndpoints_EnvironmentId",
                table: "WebhookEndpoints",
                column: "EnvironmentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Assignments");

            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "Experiments");

            migrationBuilder.DropTable(
                name: "WebhookDeliveries");

            migrationBuilder.DropTable(
                name: "WebhookEndpoints");
        }
    }
}

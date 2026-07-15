using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Featly.Storage.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookCircuitBreaker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CircuitOpenUntil",
                table: "WebhookEndpoints",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConsecutiveFailures",
                table: "WebhookEndpoints",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CircuitOpenUntil",
                table: "WebhookEndpoints");

            migrationBuilder.DropColumn(
                name: "ConsecutiveFailures",
                table: "WebhookEndpoints");
        }
    }
}

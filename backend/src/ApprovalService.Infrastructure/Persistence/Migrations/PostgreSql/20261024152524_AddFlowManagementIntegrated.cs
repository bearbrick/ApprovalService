using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApprovalService.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddFlowManagementIntegrated : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsEnabled",
                table: "Flows",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerDepartmentId",
                table: "Flows",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FlowAdministratorIdentityIssues",
                columns: table => new
                {
                    FlowId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LegacyValue = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MatchCount = table.Column<int>(type: "integer", nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlowAdministratorIdentityIssues", x => x.FlowId);
                });

            migrationBuilder.CreateTable(
                name: "FlowDrafts",
                columns: table => new
                {
                    FlowId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    DefinitionJson = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlowDrafts", x => x.FlowId);
                    table.ForeignKey(
                        name: "FK_FlowDrafts_Flows_FlowId",
                        column: x => x.FlowId,
                        principalTable: "Flows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FlowManagementAudits",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FlowId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Action = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DraftRevision = table.Column<long>(type: "bigint", nullable: true),
                    PublishedVersion = table.Column<int>(type: "integer", nullable: true),
                    ContentSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlowManagementAudits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FlowManagementAudits_FlowId_OccurredAt",
                table: "FlowManagementAudits",
                columns: new[] { "FlowId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FlowAdministratorIdentityIssues");

            migrationBuilder.DropTable(
                name: "FlowDrafts");

            migrationBuilder.DropTable(
                name: "FlowManagementAudits");

            migrationBuilder.DropColumn(
                name: "IsEnabled",
                table: "Flows");

            migrationBuilder.DropColumn(
                name: "OwnerDepartmentId",
                table: "Flows");
        }
    }
}

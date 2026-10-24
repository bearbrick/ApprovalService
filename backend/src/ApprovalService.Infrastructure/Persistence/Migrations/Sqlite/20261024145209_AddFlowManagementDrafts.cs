using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApprovalService.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddFlowManagementDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsEnabled",
                table: "Flows",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "FlowDrafts",
                columns: table => new
                {
                    FlowId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    DefinitionJson = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
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
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FlowId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DraftRevision = table.Column<long>(type: "INTEGER", nullable: true),
                    PublishedVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    ContentSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false)
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
                name: "FlowDrafts");

            migrationBuilder.DropTable(
                name: "FlowManagementAudits");

            migrationBuilder.DropColumn(
                name: "IsEnabled",
                table: "Flows");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApprovalService.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddFlowAdministratorIdentityIssues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FlowAdministratorIdentityIssues",
                columns: table => new
                {
                    FlowId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    LegacyValue = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    MatchCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlowAdministratorIdentityIssues", x => x.FlowId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FlowAdministratorIdentityIssues");
        }
    }
}

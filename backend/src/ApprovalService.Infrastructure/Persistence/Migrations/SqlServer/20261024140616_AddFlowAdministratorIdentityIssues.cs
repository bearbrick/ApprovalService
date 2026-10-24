using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApprovalService.Infrastructure.Persistence.Migrations.SqlServer
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
                    FlowId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LegacyValue = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MatchCount = table.Column<int>(type: "int", nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
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

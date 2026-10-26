using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApprovalService.Infrastructure.Persistence.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class AddDelegationRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DelegationRules",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PrincipalId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DelegateId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FlowCodesJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelegationRules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DelegationRules_PrincipalId_StartsAtUtc_EndsAtUtc",
                table: "DelegationRules",
                columns: new[] { "PrincipalId", "StartsAtUtc", "EndsAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DelegationRules");
        }
    }
}

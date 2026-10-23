using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApprovalService.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddActiveInstanceKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Instances_FlowCode_BusinessKey",
                table: "Instances");

            migrationBuilder.CreateTable(
                name: "ActiveInstanceKeys",
                columns: table => new
                {
                    FlowCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    BusinessKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    InstanceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActiveInstanceKeys", x => new { x.FlowCode, x.BusinessKey });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Instances_FlowCode_BusinessKey",
                table: "Instances",
                columns: new[] { "FlowCode", "BusinessKey" });

            migrationBuilder.CreateIndex(
                name: "IX_ActiveInstanceKeys_InstanceId",
                table: "ActiveInstanceKeys",
                column: "InstanceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActiveInstanceKeys");

            migrationBuilder.DropIndex(
                name: "IX_Instances_FlowCode_BusinessKey",
                table: "Instances");

            migrationBuilder.CreateIndex(
                name: "IX_Instances_FlowCode_BusinessKey",
                table: "Instances",
                columns: new[] { "FlowCode", "BusinessKey" },
                unique: true);
        }
    }
}

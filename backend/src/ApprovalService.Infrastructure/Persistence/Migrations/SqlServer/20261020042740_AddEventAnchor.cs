using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApprovalService.Infrastructure.Persistence.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class AddEventAnchor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastEventHash",
                table: "Instances",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "LastEventSequence",
                table: "Instances",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastEventHash",
                table: "Instances");

            migrationBuilder.DropColumn(
                name: "LastEventSequence",
                table: "Instances");
        }
    }
}

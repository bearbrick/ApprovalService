using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApprovalService.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddWorkCalendarIntegrated : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkCalendar",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    WorkingStart = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    WorkingEnd = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    OverridesJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkCalendar", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkCalendar");
        }
    }
}

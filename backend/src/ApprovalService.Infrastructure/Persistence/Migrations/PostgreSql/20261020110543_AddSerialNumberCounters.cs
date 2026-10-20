using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApprovalService.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddSerialNumberCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SerialNumberCounters",
                columns: table => new
                {
                    Prefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DateBucket = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    NextSequence = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SerialNumberCounters", x => new { x.Prefix, x.DateBucket });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SerialNumberCounters");
        }
    }
}

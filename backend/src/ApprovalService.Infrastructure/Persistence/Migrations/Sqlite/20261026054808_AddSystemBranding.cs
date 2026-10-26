using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApprovalService.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddSystemBranding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SystemBranding",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CompanyName = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    LogoBytes = table.Column<byte[]>(type: "BLOB", nullable: true),
                    LogoContentType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    LogoSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemBranding", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemBranding");
        }
    }
}

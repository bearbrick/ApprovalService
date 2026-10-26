using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApprovalService.Infrastructure.Persistence.Migrations.SqlServer
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
                    Id = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CompanyName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    LogoBytes = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    LogoContentType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    LogoSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
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

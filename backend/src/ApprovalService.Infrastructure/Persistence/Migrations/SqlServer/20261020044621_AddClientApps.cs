using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApprovalService.Infrastructure.Persistence.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class AddClientApps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClientApps",
                columns: table => new
                {
                    Code = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ApiKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SigningSecretCiphertext = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AllowedFlowCodesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CanViewAllTasks = table.Column<bool>(type: "bit", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CallbackUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    DataUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    ValidationUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    AssigneeUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    ActionUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientApps", x => x.Code);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClientApps_ApiKeyHash",
                table: "ClientApps",
                column: "ApiKeyHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientApps");
        }
    }
}

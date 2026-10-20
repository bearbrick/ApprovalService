using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApprovalService.Infrastructure.Persistence.Migrations.Sqlite
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
                    Code = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ApiKeyHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SigningSecretCiphertext = table.Column<string>(type: "TEXT", nullable: false),
                    AllowedFlowCodesJson = table.Column<string>(type: "TEXT", nullable: false),
                    CanViewAllTasks = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CallbackUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    DataUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    ValidationUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    AssigneeUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    ActionUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true)
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

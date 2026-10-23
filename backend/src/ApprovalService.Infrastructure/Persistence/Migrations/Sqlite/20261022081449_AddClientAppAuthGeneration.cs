using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApprovalService.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddClientAppAuthGeneration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AuthGeneration",
                table: "RefreshTokens",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AuthGeneration",
                table: "EmbedTokens",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AuthGeneration",
                table: "ClientApps",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AuthGeneration",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "AuthGeneration",
                table: "EmbedTokens");

            migrationBuilder.DropColumn(
                name: "AuthGeneration",
                table: "ClientApps");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApprovalService.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class CenterSignatureAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SignatureAssets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PersonId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    IsPersonal = table.Column<bool>(type: "INTEGER", nullable: false),
                    PngBytes = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignatureAssets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SignatureAssets_PersonId_IsPersonal_UploadedAt",
                table: "SignatureAssets",
                columns: new[] { "PersonId", "IsPersonal", "UploadedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SignatureAssets");
        }
    }
}

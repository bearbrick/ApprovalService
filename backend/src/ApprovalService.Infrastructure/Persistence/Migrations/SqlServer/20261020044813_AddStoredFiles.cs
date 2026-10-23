using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApprovalService.Infrastructure.Persistence.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class AddStoredFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoredFiles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StorageKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    InstanceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UploadedById = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LinkedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CleanupOwner = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CleanupLeaseUntil = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoredFiles_Instances_InstanceId",
                        column: x => x.InstanceId,
                        principalTable: "Instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_InstanceId",
                table: "StoredFiles",
                column: "InstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_StorageKind_StorageKey",
                table: "StoredFiles",
                columns: new[] { "StorageKind", "StorageKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_UploadedAt_CleanupLeaseUntil",
                table: "StoredFiles",
                columns: new[] { "UploadedAt", "CleanupLeaseUntil" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoredFiles");
        }
    }
}

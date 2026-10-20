using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApprovalService.Infrastructure.Persistence.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class AddOutboundSendLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutboundSendLogs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EventId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ClientAppCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Attempt = table.Column<int>(type: "int", nullable: false),
                    TargetUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    HttpStatus = table.Column<int>(type: "int", nullable: true),
                    Succeeded = table.Column<bool>(type: "bit", nullable: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboundSendLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboundSendLogs_EventId_Attempt",
                table: "OutboundSendLogs",
                columns: new[] { "EventId", "Attempt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutboundSendLogs");
        }
    }
}

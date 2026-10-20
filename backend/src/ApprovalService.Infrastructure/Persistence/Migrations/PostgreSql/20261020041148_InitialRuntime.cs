using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApprovalService.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class InitialRuntime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Flows",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Group = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AdministratorId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PublishedVersionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InstanceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EventId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    AvailableAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LeaseOwner = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LeaseUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScheduledJobs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InstanceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    DueAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LeaseOwner = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LeaseUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FlowVersions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FlowId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    DefinitionJson = table.Column<string>(type: "text", nullable: false),
                    GraphJson = table.Column<string>(type: "text", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlowVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FlowVersions_Flows_FlowId",
                        column: x => x.FlowId,
                        principalTable: "Flows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Instances",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SerialNo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FlowId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FlowVersionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FlowCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    BusinessKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    InitiatorId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    InitiatorDepartmentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SubmittedById = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ClientAppCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ApprovalDataJson = table.Column<string>(type: "text", nullable: false),
                    DetailUrl = table.Column<string>(type: "text", nullable: true),
                    IsUrgent = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    StateJson = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ParentInstanceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Instances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Instances_FlowVersions_FlowVersionId",
                        column: x => x.FlowVersionId,
                        principalTable: "FlowVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalTasks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InstanceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NodeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AssigneeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DelegatedForId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    Opinion = table.Column<string>(type: "text", nullable: true),
                    SignatureFileId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DueAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalTasks_Instances_InstanceId",
                        column: x => x.InstanceId,
                        principalTable: "Instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OperationEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InstanceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    RequestId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    PreviousHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperationEvents_Instances_InstanceId",
                        column: x => x.InstanceId,
                        principalTable: "Instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTasks_AssigneeId_Status_DueAt",
                table: "ApprovalTasks",
                columns: new[] { "AssigneeId", "Status", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTasks_InstanceId_NodeId",
                table: "ApprovalTasks",
                columns: new[] { "InstanceId", "NodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_Flows_Code",
                table: "Flows",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FlowVersions_FlowId_Version",
                table: "FlowVersions",
                columns: new[] { "FlowId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Instances_FlowCode_BusinessKey",
                table: "Instances",
                columns: new[] { "FlowCode", "BusinessKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Instances_FlowVersionId",
                table: "Instances",
                column: "FlowVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Instances_SerialNo",
                table: "Instances",
                column: "SerialNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Instances_Status_StartedAt",
                table: "Instances",
                columns: new[] { "Status", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationEvents_InstanceId_Sequence",
                table: "OperationEvents",
                columns: new[] { "InstanceId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxItems_EventId",
                table: "OutboxItems",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxItems_Status_AvailableAt_LeaseUntil",
                table: "OutboxItems",
                columns: new[] { "Status", "AvailableAt", "LeaseUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledJobs_Status_DueAt_LeaseUntil",
                table: "ScheduledJobs",
                columns: new[] { "Status", "DueAt", "LeaseUntil" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalTasks");

            migrationBuilder.DropTable(
                name: "OperationEvents");

            migrationBuilder.DropTable(
                name: "OutboxItems");

            migrationBuilder.DropTable(
                name: "ScheduledJobs");

            migrationBuilder.DropTable(
                name: "Instances");

            migrationBuilder.DropTable(
                name: "FlowVersions");

            migrationBuilder.DropTable(
                name: "Flows");
        }
    }
}

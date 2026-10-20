using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApprovalService.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddOrganizationIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ParentId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    HeadPersonId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    DivisionLeaderIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                    TrustedSource = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DepartureNotices",
                columns: table => new
                {
                    PersonId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PendingTaskCount = table.Column<int>(type: "INTEGER", nullable: false),
                    AcknowledgedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepartureNotices", x => x.PersonId);
                });

            migrationBuilder.CreateTable(
                name: "EmbedTokens",
                columns: table => new
                {
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ClientAppCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ExternalUserId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    PersonId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TargetPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmbedTokens", x => x.TokenHash);
                });

            migrationBuilder.CreateTable(
                name: "ExternalDepartmentBindings",
                columns: table => new
                {
                    ClientAppCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ExternalDepartmentId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DepartmentId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Method = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalDepartmentBindings", x => new { x.ClientAppCode, x.ExternalDepartmentId });
                });

            migrationBuilder.CreateTable(
                name: "ExternalPersonBindings",
                columns: table => new
                {
                    ClientAppCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ExternalUserId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PersonId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Method = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalPersonBindings", x => new { x.ClientAppCode, x.ExternalUserId });
                });

            migrationBuilder.CreateTable(
                name: "LocalAccounts",
                columns: table => new
                {
                    PersonId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    PasswordSalt = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    MustChangePassword = table.Column<bool>(type: "INTEGER", nullable: false),
                    TokenVersion = table.Column<long>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    FailedLogins = table.Column<int>(type: "INTEGER", nullable: false),
                    LockedUntil = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalAccounts", x => x.PersonId);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationChanges",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EntityKind = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    EntityId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ActorId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    BeforeJson = table.Column<string>(type: "TEXT", nullable: true),
                    AfterJson = table.Column<string>(type: "TEXT", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationChanges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationRoles",
                columns: table => new
                {
                    Code = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationRoles", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "People",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EmployeeNumber = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ManagerId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Position = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Grade = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    GradeRank = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    TrustedSource = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_People", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PersonDepartments",
                columns: table => new
                {
                    PersonId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DepartmentId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IsPrimary = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonDepartments", x => new { x.PersonId, x.DepartmentId });
                });

            migrationBuilder.CreateTable(
                name: "PersonRoles",
                columns: table => new
                {
                    PersonId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RoleCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonRoles", x => new { x.PersonId, x.RoleCode });
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PersonId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ClientAppCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ExternalUserId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    TokenVersion = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.TokenHash);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Code",
                table: "Departments",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmbedTokens_PersonId_ExpiresAt",
                table: "EmbedTokens",
                columns: new[] { "PersonId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalDepartmentBindings_DepartmentId",
                table: "ExternalDepartmentBindings",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalPersonBindings_PersonId",
                table: "ExternalPersonBindings",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_LocalAccounts_LockedUntil",
                table: "LocalAccounts",
                column: "LockedUntil");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationChanges_EntityKind_EntityId_OccurredAt",
                table: "OrganizationChanges",
                columns: new[] { "EntityKind", "EntityId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_People_EmployeeNumber",
                table: "People",
                column: "EmployeeNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_People_Phone",
                table: "People",
                column: "Phone");

            migrationBuilder.CreateIndex(
                name: "IX_PersonDepartments_DepartmentId",
                table: "PersonDepartments",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonRoles_RoleCode",
                table: "PersonRoles",
                column: "RoleCode");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_PersonId_ExpiresAt",
                table: "RefreshTokens",
                columns: new[] { "PersonId", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropTable(
                name: "DepartureNotices");

            migrationBuilder.DropTable(
                name: "EmbedTokens");

            migrationBuilder.DropTable(
                name: "ExternalDepartmentBindings");

            migrationBuilder.DropTable(
                name: "ExternalPersonBindings");

            migrationBuilder.DropTable(
                name: "LocalAccounts");

            migrationBuilder.DropTable(
                name: "OrganizationChanges");

            migrationBuilder.DropTable(
                name: "OrganizationRoles");

            migrationBuilder.DropTable(
                name: "People");

            migrationBuilder.DropTable(
                name: "PersonDepartments");

            migrationBuilder.DropTable(
                name: "PersonRoles");

            migrationBuilder.DropTable(
                name: "RefreshTokens");
        }
    }
}

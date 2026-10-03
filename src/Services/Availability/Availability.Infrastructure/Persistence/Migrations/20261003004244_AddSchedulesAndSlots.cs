using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Availability.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedulesAndSlots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Branches",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BranchWorkingHours",
                columns: table => new
                {
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpensAt = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    ClosesAt = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BranchWorkingHours", x => new { x.BranchId, x.DayOfWeek });
                    table.ForeignKey(
                        name: "FK_BranchWorkingHours_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Slots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    ReservedCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Slots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Slots_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Slots_ServiceTypes_ServiceTypeId",
                        column: x => x.ServiceTypeId,
                        principalTable: "ServiceTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Slots_BranchId_ServiceTypeId_LocalDate",
                table: "Slots",
                columns: new[] { "BranchId", "ServiceTypeId", "LocalDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Slots_BranchId_ServiceTypeId_StartUtc",
                table: "Slots",
                columns: new[] { "BranchId", "ServiceTypeId", "StartUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Slots_ServiceTypeId",
                table: "Slots",
                column: "ServiceTypeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BranchWorkingHours");

            migrationBuilder.DropTable(
                name: "Slots");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Branches");
        }
    }
}

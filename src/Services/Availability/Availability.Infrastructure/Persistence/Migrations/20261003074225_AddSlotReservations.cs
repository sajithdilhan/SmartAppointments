using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Availability.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSlotReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SlotReservations",
                columns: table => new
                {
                    SlotId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlotReservations", x => new { x.SlotId, x.AppointmentId });
                    table.ForeignKey(
                        name: "FK_SlotReservations_Slots_SlotId",
                        column: x => x.SlotId,
                        principalTable: "Slots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Slots_ReservedCount_NonNegative",
                table: "Slots",
                sql: "\"ReservedCount\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Slots_ReservedCount_WithinCapacity",
                table: "Slots",
                sql: "\"ReservedCount\" <= \"Capacity\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SlotReservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Slots_ReservedCount_NonNegative",
                table: "Slots");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Slots_ReservedCount_WithinCapacity",
                table: "Slots");
        }
    }
}

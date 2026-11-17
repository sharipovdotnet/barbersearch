using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSearch.Infrastructure.Postgresql.Migrations
{
    /// <inheritdoc />
    public partial class AddPushBookingStatusEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_push_notifications_booking_id",
                table: "push_notifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_push_notifications_kind",
                table: "push_notifications");

            migrationBuilder.CreateIndex(
                name: "ux_push_notifications_booking_id",
                table: "push_notifications",
                columns: new[] { "booking_id", "kind", "start_at" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_push_notifications_kind",
                table: "push_notifications",
                sql: "kind IN (1, 2, 3, 4, 5, 6)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_push_notifications_booking_id",
                table: "push_notifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_push_notifications_kind",
                table: "push_notifications");

            migrationBuilder.CreateIndex(
                name: "ux_push_notifications_booking_id",
                table: "push_notifications",
                columns: new[] { "booking_id", "kind" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_push_notifications_kind",
                table: "push_notifications",
                sql: "kind IN (1, 2, 3)");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSearch.Infrastructure.Postgresql.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerBookingPushEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_push_notifications_kind",
                table: "push_notifications");

            migrationBuilder.AddCheckConstraint(
                name: "ck_push_notifications_kind",
                table: "push_notifications",
                sql: "kind IN (1, 2, 3, 4, 5, 6, 7, 8)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_push_notifications_kind",
                table: "push_notifications");

            migrationBuilder.AddCheckConstraint(
                name: "ck_push_notifications_kind",
                table: "push_notifications",
                sql: "kind IN (1, 2, 3, 4, 5, 6)");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSearch.Infrastructure.Postgresql.Migrations
{
    /// <inheritdoc />
    public partial class AddBarberBookingPushNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_push_notifications_kind",
                table: "push_notifications");

            migrationBuilder.AddColumn<string>(
                name: "customer_name",
                table: "push_notifications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_push_notifications_kind",
                table: "push_notifications",
                sql: "kind IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_push_notifications_kind",
                table: "push_notifications");

            migrationBuilder.DropColumn(
                name: "customer_name",
                table: "push_notifications");

            migrationBuilder.AddCheckConstraint(
                name: "ck_push_notifications_kind",
                table: "push_notifications",
                sql: "kind IN (1, 2, 3, 4, 5, 6, 7, 8)");
        }
    }
}

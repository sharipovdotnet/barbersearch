using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSearch.Infrastructure.Postgresql.Migrations
{
    /// <inheritdoc />
    public partial class AddPushNotificationKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_push_notifications_booking_id",
                table: "push_notifications");

            migrationBuilder.AddColumn<short>(
                name: "kind",
                table: "push_notifications",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            // DEFAULT нужен только чтобы заполнить уже существующие строки видом Reminder (1).
            // Дальше значение приходит из домена явно: постоянный DEFAULT скрыл бы пропущенную
            // колонку в INSERT и позволил бы создать строку «по умолчанию».
            migrationBuilder.Sql(
                "ALTER TABLE push_notifications ALTER COLUMN kind DROP DEFAULT;");

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_push_notifications_booking_id",
                table: "push_notifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_push_notifications_kind",
                table: "push_notifications");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "push_notifications");

            migrationBuilder.CreateIndex(
                name: "ux_push_notifications_booking_id",
                table: "push_notifications",
                column: "booking_id",
                unique: true);
        }
    }
}

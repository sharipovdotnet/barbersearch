using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSearch.Infrastructure.Postgresql.Migrations
{
    /// <inheritdoc />
    public partial class AddBarberNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "barber_notification_settings",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    digest_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    digest_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    lead_minutes = table.Column<int>(type: "integer", nullable: false),
                    telegram_chat_id = table.Column<long>(type: "bigint", nullable: true),
                    telegram_linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    telegram_link_code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    telegram_link_code_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_barber_notification_settings", x => x.user_id);
                    table.CheckConstraint("ck_barber_notification_settings_digest_time", "digest_time >= TIME '06:00:00' AND digest_time <= TIME '12:00:00' AND date_part('minute', digest_time)::int % 15 = 0 AND date_part('second', digest_time) = 0");
                    table.CheckConstraint("ck_barber_notification_settings_lead_minutes", "lead_minutes IN (15, 30, 60)");
                    table.CheckConstraint("ck_barber_notification_settings_telegram_code", "(telegram_link_code_hash IS NULL) = (telegram_link_code_expires_at IS NULL) AND (telegram_link_code_hash IS NULL OR char_length(telegram_link_code_hash) = 64)");
                    table.ForeignKey(
                        name: "fk_barber_notification_settings_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "barber_notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: true),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    service_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    salon_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    salon_time_zone_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    bookings_count = table.Column<int>(type: "integer", nullable: true),
                    digest_local_date = table.Column<DateOnly>(type: "date", nullable: true),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivery = table.Column<short>(type: "smallint", nullable: false),
                    delivery_attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_barber_notifications", x => x.id);
                    table.CheckConstraint("ck_barber_notifications_delivery", "delivery IN (0, 1, 2, 3)");
                    table.CheckConstraint("ck_barber_notifications_delivery_attempts", "delivery_attempts BETWEEN 0 AND 5");
                    table.CheckConstraint("ck_barber_notifications_kind", "kind IN (1, 2)");
                    table.CheckConstraint("ck_barber_notifications_shape", "(kind = 1 AND booking_id IS NOT NULL AND start_at IS NOT NULL)\nOR (kind = 2 AND bookings_count IS NOT NULL AND digest_local_date IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "telegram_updates",
                columns: table => new
                {
                    update_id = table.Column<long>(type: "bigint", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_telegram_updates", x => x.update_id);
                });

            // Индекс скана подтверждённых броней для напоминаний. В модели его не было
            // (BookingConfiguration), поэтому миграция добавляет его здесь; частичный
            // фильтр повторяет предикат напоминания (Pending + Confirmed). В базе уже есть
            // ix_bookings_status_start_at_id ((status, start_at DESC, id)), созданный
            // AddAdminBookingsStatusIndex сырым SQL; этот индекс — более узкий вариант
            // для чтения только по статусу и диапазону начала.
            migrationBuilder.CreateIndex(
                name: "ix_bookings_status_start_at",
                table: "bookings",
                columns: new[] { "status", "start_at" },
                filter: "status IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "ux_barber_notification_settings_telegram_chat_id",
                table: "barber_notification_settings",
                column: "telegram_chat_id",
                unique: true,
                filter: "telegram_chat_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_barber_notification_settings_telegram_link_code_hash",
                table: "barber_notification_settings",
                column: "telegram_link_code_hash",
                unique: true,
                filter: "telegram_link_code_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_barber_notifications_delivery",
                table: "barber_notifications",
                columns: new[] { "next_attempt_at", "created_at" },
                filter: "delivery IN (0, 3)");

            migrationBuilder.CreateIndex(
                name: "ix_barber_notifications_user_created",
                table: "barber_notifications",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_barber_notifications_digest",
                table: "barber_notifications",
                columns: new[] { "user_id", "digest_local_date" },
                unique: true,
                filter: "kind = 2");

            migrationBuilder.CreateIndex(
                name: "ux_barber_notifications_upcoming",
                table: "barber_notifications",
                column: "booking_id",
                unique: true,
                filter: "kind = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "barber_notification_settings");

            migrationBuilder.DropTable(
                name: "barber_notifications");

            migrationBuilder.DropTable(
                name: "telegram_updates");

            migrationBuilder.DropIndex(
                name: "ix_bookings_status_start_at",
                table: "bookings");
        }
    }
}

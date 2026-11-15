using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSearch.Infrastructure.Postgresql.Migrations;

/// <inheritdoc />
public partial class AddPushNotifications : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "push_device_tokens",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                token = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                platform = table.Column<short>(type: "smallint", nullable: false),
                last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_push_device_tokens", x => x.id);
                table.CheckConstraint("ck_push_device_tokens_platform", "platform IN (1, 2)");
                table.CheckConstraint("ck_push_device_tokens_token_length", "char_length(token) BETWEEN 1 AND 512");
                table.ForeignKey(
                    name: "fk_push_device_tokens_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "push_notifications",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                salon_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                barber_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                service_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                salon_time_zone_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                delivery = table.Column<short>(type: "smallint", nullable: false),
                delivery_attempts = table.Column<int>(type: "integer", nullable: false),
                next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_push_notifications", x => x.id);
                table.CheckConstraint("ck_push_notifications_delivery", "delivery IN (0, 1, 2, 3)");
                table.CheckConstraint("ck_push_notifications_delivery_attempts", "delivery_attempts BETWEEN 0 AND 5");
                table.ForeignKey(
                    name: "fk_push_notifications_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_push_device_tokens_user_id",
            table: "push_device_tokens",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name: "ux_push_device_tokens_token",
            table: "push_device_tokens",
            column: "token",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_push_notifications_dispatch",
            table: "push_notifications",
            column: "created_at",
            filter: "delivery = 0 AND delivery_attempts < 5");

        migrationBuilder.CreateIndex(
            name: "ix_push_notifications_user_id",
            table: "push_notifications",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name: "ux_push_notifications_booking_id",
            table: "push_notifications",
            column: "booking_id",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "push_device_tokens");

        migrationBuilder.DropTable(
            name: "push_notifications");
    }
}

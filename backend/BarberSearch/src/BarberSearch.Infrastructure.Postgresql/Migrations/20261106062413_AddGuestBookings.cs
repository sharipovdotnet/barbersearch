using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSearch.Infrastructure.Postgresql.Migrations
{
    /// <inheritdoc />
    public partial class AddGuestBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "bookings",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "guest_first_name",
                table: "bookings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "guest_last_name",
                table: "bookings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "guest_phone",
                table: "bookings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_bookings_barber_profile_id_idempotency_key_guest",
                table: "bookings",
                columns: new[] { "barber_profile_id", "idempotency_key" },
                unique: true,
                filter: "user_id IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bookings_customer",
                table: "bookings",
                sql: "(user_id IS NOT NULL AND guest_first_name IS NULL AND guest_last_name IS NULL AND guest_phone IS NULL) OR (user_id IS NULL AND guest_first_name IS NOT NULL AND guest_phone IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bookings_guest_first_name",
                table: "bookings",
                sql: "guest_first_name IS NULL OR char_length(btrim(guest_first_name)) BETWEEN 1 AND 100");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bookings_guest_last_name",
                table: "bookings",
                sql: "guest_last_name IS NULL OR char_length(btrim(guest_last_name)) BETWEEN 1 AND 100");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bookings_guest_phone",
                table: "bookings",
                sql: "guest_phone IS NULL OR char_length(btrim(guest_phone)) BETWEEN 1 AND 32");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_bookings_barber_profile_id_idempotency_key_guest",
                table: "bookings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bookings_customer",
                table: "bookings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bookings_guest_first_name",
                table: "bookings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bookings_guest_last_name",
                table: "bookings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bookings_guest_phone",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "guest_first_name",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "guest_last_name",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "guest_phone",
                table: "bookings");

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "bookings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}

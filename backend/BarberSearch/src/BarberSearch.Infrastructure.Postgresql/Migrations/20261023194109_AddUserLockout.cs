using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSearch.Infrastructure.Postgresql.Migrations
{
    /// <inheritdoc />
    public partial class AddUserLockout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "failed_login_attempts",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Значение по умолчанию нужно было только на время backfill: новые строки
            // получают счётчик из домена (0 — значение по умолчанию у int), а не из базы.
            migrationBuilder.Sql(
                "ALTER TABLE users ALTER COLUMN failed_login_attempts DROP DEFAULT;");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_failed_login_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lockout_end",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_failed_login_attempts",
                table: "users",
                sql: "failed_login_attempts >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_users_failed_login_attempts",
                table: "users");

            migrationBuilder.DropColumn(
                name: "failed_login_attempts",
                table: "users");

            migrationBuilder.DropColumn(
                name: "last_failed_login_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "lockout_end",
                table: "users");
        }
    }
}

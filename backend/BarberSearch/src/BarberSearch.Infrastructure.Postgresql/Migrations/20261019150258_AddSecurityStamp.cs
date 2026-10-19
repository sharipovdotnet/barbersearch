using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSearch.Infrastructure.Postgresql.Migrations
{
    /// <inheritdoc />
    public partial class AddSecurityStamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "security_stamp",
                table: "users",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Существующим строкам выдаётся уникальное непустое значение: пустой stamp
            // не прошёл бы ck_users_security_stamp. gen_random_uuid() встроен в PostgreSQL 13+.
            migrationBuilder.Sql(
                """
                UPDATE users
                SET security_stamp = gen_random_uuid()::text
                WHERE security_stamp = '';
                """);

            // Значение по умолчанию нужно было только на время backfill: новые строки
            // получают stamp из домена, а не из базы.
            migrationBuilder.Sql(
                "ALTER TABLE users ALTER COLUMN security_stamp DROP DEFAULT;");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_security_stamp",
                table: "users",
                sql: "char_length(security_stamp) > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_users_security_stamp",
                table: "users");

            migrationBuilder.DropColumn(
                name: "security_stamp",
                table: "users");
        }
    }
}

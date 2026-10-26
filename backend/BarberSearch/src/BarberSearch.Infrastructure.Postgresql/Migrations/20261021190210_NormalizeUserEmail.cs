using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSearch.Infrastructure.Postgresql.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeUserEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_users_email",
                table: "users");

            // Существующие строки могли быть записаны с пробелами и в верхнем регистре.
            // Приводим их к каноническому виду до смены типа: citext сделает уникальный
            // индекс регистронезависимым, и пара "User@x"/"user@x" уронила бы ALTER
            // невнятной ошибкой уникальности. Проверки ниже превращают такую ситуацию
            // в понятное сообщение, а не в полуприменённую миграцию.
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE duplicate_email text;
                BEGIN
                    SELECT lower(btrim(email)) INTO duplicate_email
                    FROM users
                    GROUP BY lower(btrim(email))
                    HAVING count(*) > 1
                    LIMIT 1;

                    IF duplicate_email IS NOT NULL THEN
                        RAISE EXCEPTION
                            'Cannot normalize users.email: address % occurs more than once',
                            duplicate_email;
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM users
                        WHERE char_length(btrim(email)) NOT BETWEEN 3 AND 254)
                    THEN
                        RAISE EXCEPTION
                            'Cannot normalize users.email: an address is outside 3..254 characters';
                    END IF;

                    UPDATE users
                    SET email = lower(btrim(email))
                    WHERE email <> lower(btrim(email));
                END $$;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "email",
                table: "users",
                type: "citext",
                maxLength: 254,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_email",
                table: "users",
                sql: "char_length(email) BETWEEN 3 AND 254");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_users_email",
                table: "users");

            migrationBuilder.AlterColumn<string>(
                name: "email",
                table: "users",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "citext",
                oldMaxLength: 254);

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_email",
                table: "users",
                sql: "position('@' in email) > 1");
        }
    }
}

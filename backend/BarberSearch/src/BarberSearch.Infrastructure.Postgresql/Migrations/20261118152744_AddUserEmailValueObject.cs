using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSearch.Infrastructure.Postgresql.Migrations
{
    /// <summary>
    /// Адрес пользователя стал объектом-значением <c>Email</c>, но схема не меняется:
    /// каноническое значение хранится в той же колонке <c>users.email</c> (<c>citext</c>,
    /// NOT NULL, 254 символа), а конвертер отдаёт <c>Email.Value</c> и собирает
    /// <c>Email.FromStorage</c> при чтении. Индекс <c>ux_users_email</c> и check-ограничение
    /// <c>ck_users_email</c> остаются прежними. Миграция пустая и нужна только для
    /// обновления снапшота модели.
    /// </summary>
    public partial class AddUserEmailValueObject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSearch.Infrastructure.Postgresql.Migrations
{
    /// <summary>
    /// Телефон пользователя стал owned-типом <c>PhoneNumber</c>, но схема не меняется:
    /// каноническое значение хранится в той же nullable text-колонке <c>users.phone_number</c>.
    /// Миграция пустая и нужна только для обновления снапшота модели.
    /// </summary>
    public partial class AddUserPhoneNumberValueObject : Migration
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

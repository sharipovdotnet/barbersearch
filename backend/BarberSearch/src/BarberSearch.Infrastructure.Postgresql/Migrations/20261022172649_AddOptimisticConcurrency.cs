using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSearch.Infrastructure.Postgresql.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// <c>xmin</c> — системная колонка PostgreSQL: она есть у каждой таблицы и меняется при
    /// каждом <c>UPDATE</c>, поэтому DDL не нужен. Миграция фиксирует в snapshot модели
    /// появление токенов конкурентности у сущностей агрегатов (R-05); Npgsql исключает
    /// системные колонки из генерируемого SQL.
    /// </remarks>
    public partial class AddOptimisticConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Схема не меняется: xmin существует в таблицах с момента их создания.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Схема не меняется: удалять нечего.
        }
    }
}

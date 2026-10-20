using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSearch.Infrastructure.Postgresql.Migrations
{
    /// <inheritdoc />
    public partial class AddReadIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Поиск барберов: фильтр is_active/is_verified и порядок rating DESC NULLS LAST, id.
            // NULLS LAST в EF Core не выражается через HasIndex, поэтому индекс создан SQL.
            // Частичный индекс меньше полного и совпадает с предикатом запроса.
            migrationBuilder.Sql(
                """
                CREATE INDEX ix_barber_profiles_search
                ON barber_profiles (rating DESC NULLS LAST, id)
                WHERE is_active AND is_verified;
                """);

            // Опубликованные отзывы барбера: фильтр is_published + порядок created_at DESC, id.
            migrationBuilder.Sql(
                """
                CREATE INDEX ix_reviews_barber_profile_id_published_created_at
                ON reviews (barber_profile_id, created_at DESC, id)
                WHERE is_published;
                """);

            // Администратор читает все брони: порядок start_at DESC, id.
            // Пользовательский список покрыт ix_bookings_user_id_start_at.
            migrationBuilder.Sql(
                """
                CREATE INDEX ix_bookings_start_at_id
                ON bookings (start_at DESC, id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_bookings_start_at_id;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_reviews_barber_profile_id_published_created_at;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_barber_profiles_search;");
        }
    }
}

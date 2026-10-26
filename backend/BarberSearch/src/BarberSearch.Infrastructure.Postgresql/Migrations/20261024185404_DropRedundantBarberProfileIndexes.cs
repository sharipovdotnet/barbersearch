using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSearch.Infrastructure.Postgresql.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// R-38: на <c>barber_profiles</c> было три пересекающихся индекса. Реальные планы
    /// <c>EXPLAIN (ANALYZE, BUFFERS)</c> поиска (<c>SearchAsync</c>) используют только
    /// <c>ix_barber_profiles_search_keyset</c>: его порядок
    /// <c>(((rating IS NULL)), rating DESC, id)</c> — надмножество
    /// <c>(rating DESC NULLS LAST, id)</c>, а частичный предикат совпадает у всех трёх.
    /// <c>ix_barber_profiles_is_active_is_verified_rating</c> и
    /// <c>ix_barber_profiles_search</c> не выбирались ни одним запросом
    /// (<c>pg_stat_user_indexes.idx_scan = 0</c>) и только удорожали запись.
    /// </remarks>
    public partial class DropRedundantBarberProfileIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_barber_profiles_is_active_is_verified_rating",
                table: "barber_profiles");

            // ix_barber_profiles_search создан raw SQL в AddReadIndexes и в EF-модели
            // не объявлен, поэтому сгенерированный DropIndex его не видит.
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_barber_profiles_search;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Восстанавливаем raw SQL-индекс в исходном виде (как в AddReadIndexes).
            migrationBuilder.Sql(
                """
                CREATE INDEX ix_barber_profiles_search
                ON barber_profiles (rating DESC NULLS LAST, id)
                WHERE is_active AND is_verified;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_barber_profiles_is_active_is_verified_rating",
                table: "barber_profiles",
                columns: new[] { "is_active", "is_verified", "rating" },
                descending: new[] { false, false, true });
        }
    }
}

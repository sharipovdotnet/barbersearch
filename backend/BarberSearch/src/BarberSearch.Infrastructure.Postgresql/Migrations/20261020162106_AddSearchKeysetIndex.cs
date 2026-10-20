using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSearch.Infrastructure.Postgresql.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchKeysetIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF Core translates the search order to:
            //   ORDER BY (rating IS NULL), rating DESC, id
            // The existing ix_barber_profiles_search is (rating DESC NULLS LAST, id)
            // and cannot serve that expression, so the planner falls back to a
            // sequential scan + sort of all visible profiles (LOADTEST.md §7.1).
            //
            // This expression index matches the ORDER BY term for term. In PostgreSQL
            // boolean ASC is false, true — exactly the "rated first, no-reviews last"
            // group order the query asks for. The partial predicate is the same as the
            // query and as ix_barber_profiles_search, so the index covers only public
            // profiles. Existing migrations are intentionally left untouched: this is
            // an additive index, and the old index remains for other consumers.
            migrationBuilder.Sql(
                """
                CREATE INDEX ix_barber_profiles_search_keyset
                ON barber_profiles (((rating IS NULL)), rating DESC, id)
                WHERE is_active AND is_verified;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_barber_profiles_search_keyset;");
        }
    }
}

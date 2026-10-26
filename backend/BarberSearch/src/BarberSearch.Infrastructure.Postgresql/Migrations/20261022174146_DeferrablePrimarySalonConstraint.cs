using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSearch.Infrastructure.Postgresql.Migrations
{
    /// <inheritdoc />
    public partial class DeferrablePrimarySalonConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Уникальный индекс нельзя сделать DEFERRABLE: в PostgreSQL отложенность
            // поддерживают только ограничения (R-06). Частичное exclusion-ограничение
            // USING btree даёт ровно ту же уникальность «один основной салон на профиль»,
            // но проверяется в COMMIT, поэтому SetPrimarySalon за один SaveChanges может
            // снять старый флаг и поставить новый в любом порядке.
            migrationBuilder.DropIndex(
                name: "ux_barber_salons_primary_per_profile",
                table: "barber_salons");

            migrationBuilder.Sql(
                """
                ALTER TABLE barber_salons
                    ADD CONSTRAINT ex_barber_salons_primary_per_profile
                    EXCLUDE USING btree (barber_profile_id WITH =)
                    WHERE (is_primary)
                    DEFERRABLE INITIALLY DEFERRED;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE barber_salons
                    DROP CONSTRAINT ex_barber_salons_primary_per_profile;
                """);

            migrationBuilder.CreateIndex(
                name: "ux_barber_salons_primary_per_profile",
                table: "barber_salons",
                column: "barber_profile_id",
                unique: true,
                filter: "is_primary");
        }
    }
}

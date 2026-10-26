using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSearch.Infrastructure.Postgresql.Migrations
{
    /// <inheritdoc />
    public partial class AddCrossAggregateForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOT VALID — сознательно: новые строки PostgreSQL проверяет сразу, а уже
            // накопленные висячие ссылки не роняют миграцию. Их поиск и очистка —
            // deploy/find-dangling-references.sql, после чего ограничения можно
            // проверить вручную (ALTER TABLE … VALIDATE CONSTRAINT …).
            migrationBuilder.Sql(
                """
                ALTER TABLE barber_salons
                    ADD CONSTRAINT fk_barber_salons_salons_salon_id
                    FOREIGN KEY (salon_id) REFERENCES salons (id)
                    ON DELETE RESTRICT
                    NOT VALID;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE barber_services
                    ADD CONSTRAINT fk_barber_services_hair_services_hair_service_id
                    FOREIGN KEY (hair_service_id) REFERENCES hair_services (id)
                    ON DELETE RESTRICT
                    NOT VALID;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE barber_specializations
                    ADD CONSTRAINT fk_barber_specializations_specializations_specialization_id
                    FOREIGN KEY (specialization_id) REFERENCES specializations (id)
                    ON DELETE RESTRICT
                    NOT VALID;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE favorite_barbers
                    ADD CONSTRAINT fk_favorite_barbers_barber_profiles_barber_profile_id
                    FOREIGN KEY (barber_profile_id) REFERENCES barber_profiles (id)
                    ON DELETE RESTRICT
                    NOT VALID;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE users
                    ADD CONSTRAINT fk_users_barber_profiles_barber_profile_id
                    FOREIGN KEY (barber_profile_id) REFERENCES barber_profiles (id)
                    ON DELETE RESTRICT
                    NOT VALID;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_barber_salons_salons_salon_id",
                table: "barber_salons");

            migrationBuilder.DropForeignKey(
                name: "fk_barber_services_hair_services_hair_service_id",
                table: "barber_services");

            migrationBuilder.DropForeignKey(
                name: "fk_barber_specializations_specializations_specialization_id",
                table: "barber_specializations");

            migrationBuilder.DropForeignKey(
                name: "fk_favorite_barbers_barber_profiles_barber_profile_id",
                table: "favorite_barbers");

            migrationBuilder.DropForeignKey(
                name: "fk_users_barber_profiles_barber_profile_id",
                table: "users");
        }
    }
}

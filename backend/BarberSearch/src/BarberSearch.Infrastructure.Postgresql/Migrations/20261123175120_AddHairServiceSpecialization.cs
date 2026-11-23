using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSearch.Infrastructure.Postgresql.Migrations
{
    /// <summary>
    /// Услуга каталога получает обязательное направление (<c>specializations</c>).
    /// </summary>
    /// <remarks>
    /// Колонка добавляется nullable, заполняется и только потом становится обязательной:
    /// иначе добавление NOT NULL к непустой таблице упало бы. Существующие услуги
    /// привязываются к служебному направлению «Общие услуги», которое создаётся этим же
    /// шагом; администратор перепривязывает их к реальным направлениям в каталоге.
    /// </remarks>
    public partial class AddHairServiceSpecialization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "specialization_id",
                table: "hair_services",
                type: "uuid",
                nullable: true);

            // Backfill: направление обязательно, а в существующих строках его нет.
            // Служебная строка создаётся один раз и только если есть что заполнять;
            // повторный запуск (например, на ветке) её не дублирует.
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    fallback_id uuid;
                BEGIN
                    IF EXISTS (SELECT 1 FROM hair_services WHERE specialization_id IS NULL) THEN
                        SELECT id INTO fallback_id
                        FROM specializations
                        WHERE name = 'Общие услуги'
                        LIMIT 1;

                        IF fallback_id IS NULL THEN
                            fallback_id := gen_random_uuid();

                            INSERT INTO specializations
                                (id, name, description, is_active, created_at, updated_at)
                            VALUES
                                (fallback_id,
                                 'Общие услуги',
                                 'Служебное направление, созданное миграцией AddHairServiceSpecialization: перепривяжите услуги к реальным направлениям.',
                                 true,
                                 now(),
                                 now());
                        END IF;

                        UPDATE hair_services
                        SET specialization_id = fallback_id
                        WHERE specialization_id IS NULL;
                    END IF;
                END $$;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "specialization_id",
                table: "hair_services",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_hair_services_specialization_id_is_active",
                table: "hair_services",
                columns: new[] { "specialization_id", "is_active" });

            migrationBuilder.AddForeignKey(
                name: "fk_hair_services_specializations_specialization_id",
                table: "hair_services",
                column: "specialization_id",
                principalTable: "specializations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_hair_services_specializations_specialization_id",
                table: "hair_services");

            migrationBuilder.DropIndex(
                name: "ix_hair_services_specialization_id_is_active",
                table: "hair_services");

            migrationBuilder.DropColumn(
                name: "specialization_id",
                table: "hair_services");
        }
    }
}

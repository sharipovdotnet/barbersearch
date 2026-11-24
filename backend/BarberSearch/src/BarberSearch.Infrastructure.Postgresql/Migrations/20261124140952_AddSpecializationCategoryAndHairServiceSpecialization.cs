using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSearch.Infrastructure.Postgresql.Migrations
{
    /// <summary>
    /// Категория переезжает к специализации, а услуга получает обязательную специализацию.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Порядок шагов задан вручную: <c>specializations.category</c> добавляется сразу
    /// обязательной (DEFAULT <c>99</c> «Другое» заполняет существующие строки),
    /// <c>hair_services.specialization_id</c> — сначала nullable, затем заполняется
    /// и только потом становится NOT NULL. Обратный порядок уронил бы миграцию на непустой
    /// таблице.
    /// </para>
    /// <para>
    /// Backfill: для каждой категории, которая реально встречается у услуг, находится или
    /// создаётся специализация с русским названием категории
    /// (<c>1 Стрижка, 2 Борода, 3 Окрашивание, 4 Укладка, 5 Уход, 99 Другое</c>).
    /// Существующая строка ищется без учёта регистра (<c>citext</c>) и получает категорию;
    /// новой строке достаётся детерминированный id <c>md5('catalogue-category:' || category)</c>,
    /// поэтому повторный запуск строку не дублирует. Специализации, не совпавшие по названию,
    /// остаются с «Другое» — это осознанно, администратор поправит их вручную.
    /// Старая колонка <c>hair_services.category</c> удаляется последней, после переноса данных.
    /// </para>
    /// </remarks>
    public partial class AddSpecializationCategoryAndHairServiceSpecialization : Migration
    {
        /// <summary>
        /// Названия специализаций по категориям услуг: одна и та же таблица используется
        /// при создании строк и при привязке услуг.
        /// </summary>
        private const string CATEGORY_NAMES = """
            (VALUES
                (1,  'Стрижка'),
                (2,  'Борода'),
                (3,  'Окрашивание'),
                (4,  'Укладка'),
                (5,  'Уход'),
                (99, 'Другое')
            ) AS v(category, name)
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Категория специализации: обязательна, существующие строки получают «Другое».
            migrationBuilder.AddColumn<int>(
                name: "category",
                table: "specializations",
                type: "integer",
                nullable: false,
                defaultValue: 99);

            migrationBuilder.AddCheckConstraint(
                name: "ck_specializations_category",
                table: "specializations",
                sql: "category IN (1, 2, 3, 4, 5, 99)");

            // 2. Ссылка услуги на специализацию: пока nullable, заполняется ниже.
            migrationBuilder.AddColumn<Guid>(
                name: "specialization_id",
                table: "hair_services",
                type: "uuid",
                nullable: true);

            // 3. Backfill.
            // 3.1. Существующая специализация с названием категории получает эту категорию:
            //      строку не дублируем, иначе уникальный индекс ux_specializations_name упал бы.
            //      Приведение к citext обязательно: `citext = text` в PostgreSQL — сравнение
            //      с учётом регистра, а имя справочника уникально без учёта регистра.
            migrationBuilder.Sql(
                $"""
                 UPDATE specializations AS s
                 SET category = v.category,
                     updated_at = now()
                 FROM {CATEGORY_NAMES}
                 WHERE s.name = v.name::citext
                   AND s.category <> v.category
                   AND EXISTS (
                       SELECT 1 FROM hair_services AS hs WHERE hs.category = v.category);
                 """);

            // 3.2. Специализации с названием категории нет — создаём её. id детерминирован,
            //      поэтому повторный запуск (например, на ветке) строку не дублирует.
            migrationBuilder.Sql(
                $"""
                 INSERT INTO specializations
                     (id, name, description, is_active, category, created_at, updated_at)
                 SELECT md5('catalogue-category:' || v.category)::uuid,
                        v.name,
                        NULL,
                        true,
                        v.category,
                        now(),
                        now()
                 FROM {CATEGORY_NAMES}
                 WHERE EXISTS (
                           SELECT 1 FROM hair_services AS hs WHERE hs.category = v.category)
                   AND NOT EXISTS (
                           SELECT 1 FROM specializations AS s
                           WHERE s.name = v.name::citext);
                 """);

            // 3.3. Каждая услуга привязывается к специализации своей категории.
            migrationBuilder.Sql(
                $"""
                 UPDATE hair_services AS hs
                 SET specialization_id = s.id
                 FROM {CATEGORY_NAMES}
                 JOIN specializations AS s ON s.name = v.name::citext
                 WHERE hs.category = v.category;
                 """);

            // 4. Ссылка становится обязательной, затем FK и индекс.
            migrationBuilder.AlterColumn<Guid>(
                name: "specialization_id",
                table: "hair_services",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_hair_services_specializations_specialization_id",
                table: "hair_services",
                column: "specialization_id",
                principalTable: "specializations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.CreateIndex(
                name: "ix_hair_services_specialization_id",
                table: "hair_services",
                column: "specialization_id");

            // 5. Старая колонка категории услуги больше не нужна: данные перенесены.
            migrationBuilder.DropIndex(
                name: "ix_hair_services_category_is_active",
                table: "hair_services");

            migrationBuilder.DropCheckConstraint(
                name: "ck_hair_services_category",
                table: "hair_services");

            migrationBuilder.DropColumn(
                name: "category",
                table: "hair_services");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Обратный порядок. Служебные специализации, созданные в Up, не удаляются:
            // чужие строки — не мусор миграции, их мог использовать администратор.

            // 1. Категория услуги возвращается nullable и заполняется из специализации.
            migrationBuilder.AddColumn<int>(
                name: "category",
                table: "hair_services",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE hair_services AS hs
                SET category = s.category
                FROM specializations AS s
                WHERE s.id = hs.specialization_id;
                """);

            // Подстраховка: ссылка обязательна и FK её держит, но Down не должен падать
            // на NOT NULL, если строку успели изменить вручную.
            migrationBuilder.Sql(
                "UPDATE hair_services SET category = 99 WHERE category IS NULL;");

            migrationBuilder.AlterColumn<int>(
                name: "category",
                table: "hair_services",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_hair_services_category",
                table: "hair_services",
                sql: "category IN (1, 2, 3, 4, 5, 99)");

            migrationBuilder.CreateIndex(
                name: "ix_hair_services_category_is_active",
                table: "hair_services",
                columns: new[] { "category", "is_active" });

            // 2. Ссылка на специализацию снимается.
            migrationBuilder.DropForeignKey(
                name: "fk_hair_services_specializations_specialization_id",
                table: "hair_services");

            migrationBuilder.DropIndex(
                name: "ix_hair_services_specialization_id",
                table: "hair_services");

            migrationBuilder.DropColumn(
                name: "specialization_id",
                table: "hair_services");

            // 3. Категория у специализации исчезает последней.
            migrationBuilder.DropCheckConstraint(
                name: "ck_specializations_category",
                table: "specializations");

            migrationBuilder.DropColumn(
                name: "category",
                table: "specializations");
        }
    }
}

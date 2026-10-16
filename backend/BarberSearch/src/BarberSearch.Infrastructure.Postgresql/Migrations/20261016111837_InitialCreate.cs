using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSearch.Infrastructure.Postgresql.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "barber_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    grade = table.Column<int>(type: "integer", nullable: false),
                    bio = table.Column<string>(type: "text", nullable: true),
                    languages = table.Column<int[]>(type: "integer[]", nullable: false),
                    years_of_experience = table.Column<int>(type: "integer", nullable: false),
                    certificates_and_training = table.Column<string[]>(type: "text[]", nullable: false),
                    rating = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: true),
                    reviews_count = table.Column<int>(type: "integer", nullable: false),
                    achievements = table.Column<string[]>(type: "text[]", nullable: false),
                    payment_methods = table.Column<int[]>(type: "integer[]", nullable: false),
                    communication_format = table.Column<string[]>(type: "text[]", nullable: false),
                    goodies = table.Column<string[]>(type: "text[]", nullable: false),
                    late_policy = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_verified = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_barber_profiles", x => x.id);
                    table.CheckConstraint("ck_barber_profiles_display_name", "char_length(display_name) BETWEEN 1 AND 120");
                    table.CheckConstraint("ck_barber_profiles_grade", "grade BETWEEN 0 AND 10");
                    table.CheckConstraint("ck_barber_profiles_rating", "(rating IS NULL) = (reviews_count = 0) AND (rating IS NULL OR rating BETWEEN 1.00 AND 5.00)");
                    table.CheckConstraint("ck_barber_profiles_reviews_count", "reviews_count >= 0");
                    table.CheckConstraint("ck_barber_profiles_years_of_experience", "years_of_experience BETWEEN 1 AND 70");
                });

            migrationBuilder.CreateTable(
                name: "bookings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    barber_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    barber_service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    salon_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    idempotency_key = table.Column<string>(type: "text", nullable: false),
                    customer_note = table.Column<string>(type: "text", nullable: true),
                    barber_note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bookings", x => x.id);
                    table.CheckConstraint("ck_bookings_idempotency_key", "char_length(idempotency_key) BETWEEN 1 AND 128");
                    table.CheckConstraint("ck_bookings_period", "end_at > start_at");
                    table.CheckConstraint("ck_bookings_price", "price >= 0");
                    table.CheckConstraint("ck_bookings_status", "status IN (1, 2, 3, 4, 5)");
                });

            migrationBuilder.CreateTable(
                name: "hair_services",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    category = table.Column<int>(type: "integer", nullable: false),
                    default_duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hair_services", x => x.id);
                    table.CheckConstraint("ck_hair_services_category", "category IN (1, 2, 3, 4, 5, 99)");
                    table.CheckConstraint("ck_hair_services_default_duration_minutes", "default_duration_minutes BETWEEN 1 AND 1440");
                    table.CheckConstraint("ck_hair_services_name", "char_length(btrim(name)) > 0");
                });

            migrationBuilder.CreateTable(
                name: "reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    barber_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "text", nullable: true),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reviews", x => x.id);
                    table.CheckConstraint("ck_reviews_rating", "rating BETWEEN 1 AND 5");
                    table.CheckConstraint("ck_reviews_text", "text IS NULL OR char_length(text) <= 2000");
                });

            migrationBuilder.CreateTable(
                name: "salons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    address = table.Column<string>(type: "text", nullable: false),
                    city = table.Column<string>(type: "text", nullable: false),
                    postal_code = table.Column<string>(type: "text", nullable: true),
                    country = table.Column<string>(type: "text", nullable: false),
                    time_zone_id = table.Column<string>(type: "text", nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    website_url = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_salons", x => x.id);
                    table.CheckConstraint("ck_salons_address", "char_length(btrim(address)) > 0");
                    table.CheckConstraint("ck_salons_city", "char_length(btrim(city)) > 0");
                    table.CheckConstraint("ck_salons_country", "char_length(btrim(country)) > 0");
                    table.CheckConstraint("ck_salons_latitude", "latitude BETWEEN -90 AND 90");
                    table.CheckConstraint("ck_salons_longitude", "longitude BETWEEN -180 AND 180");
                    table.CheckConstraint("ck_salons_name", "char_length(btrim(name)) > 0");
                    table.CheckConstraint("ck_salons_time_zone_id", "char_length(btrim(time_zone_id)) > 0");
                });

            migrationBuilder.CreateTable(
                name: "specializations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "citext", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_specializations", x => x.id);
                    table.CheckConstraint("ck_specializations_name", "char_length(btrim(name)) > 0");
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    first_name = table.Column<string>(type: "text", nullable: false),
                    last_name = table.Column<string>(type: "text", nullable: false),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    avatar_url = table.Column<string>(type: "text", nullable: true),
                    is_email_verified = table.Column<bool>(type: "boolean", nullable: false),
                    is_phone_verified = table.Column<bool>(type: "boolean", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    barber_profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.CheckConstraint("ck_users_email", "position('@' in email) > 1");
                    table.CheckConstraint("ck_users_role", "role IN (1, 2, 3)");
                });

            migrationBuilder.CreateTable(
                name: "barber_salons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    barber_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    salon_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_barber_salons", x => x.id);
                    table.ForeignKey(
                        name: "fk_barber_salons_barber_profiles_barber_profile_id",
                        column: x => x.barber_profile_id,
                        principalTable: "barber_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "barber_services",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    barber_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hair_service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_barber_services", x => x.id);
                    table.CheckConstraint("ck_barber_services_duration_minutes", "duration_minutes BETWEEN 1 AND 1440");
                    table.CheckConstraint("ck_barber_services_price", "price >= 0");
                    table.ForeignKey(
                        name: "fk_barber_services_barber_profiles_barber_profile_id",
                        column: x => x.barber_profile_id,
                        principalTable: "barber_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "barber_specializations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    barber_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    specialization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_barber_specializations", x => x.id);
                    table.ForeignKey(
                        name: "fk_barber_specializations_barber_profiles_barber_profile_id",
                        column: x => x.barber_profile_id,
                        principalTable: "barber_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "portfolio_photos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    barber_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "text", nullable: false),
                    thumbnail_url = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_portfolio_photos", x => x.id);
                    table.CheckConstraint("ck_portfolio_photos_sort_order", "sort_order >= 0");
                    table.CheckConstraint("ck_portfolio_photos_url", "char_length(btrim(url)) > 0");
                    table.ForeignKey(
                        name: "fk_portfolio_photos_barber_profiles_barber_profile_id",
                        column: x => x.barber_profile_id,
                        principalTable: "barber_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "time_offs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    barber_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_time_offs", x => x.id);
                    table.CheckConstraint("ck_time_offs_period", "end_at > start_at");
                    table.ForeignKey(
                        name: "fk_time_offs_barber_profiles_barber_profile_id",
                        column: x => x.barber_profile_id,
                        principalTable: "barber_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "favorite_barbers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    barber_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_favorite_barbers", x => x.id);
                    table.ForeignKey(
                        name: "fk_favorite_barbers_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "working_schedules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    barber_salon_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_of_week = table.Column<int>(type: "integer", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    is_day_off = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_working_schedules", x => x.id);
                    table.CheckConstraint("ck_working_schedules_day_of_week", "day_of_week BETWEEN 0 AND 6");
                    table.CheckConstraint("ck_working_schedules_time_range", "is_day_off OR end_time > start_time");
                    table.ForeignKey(
                        name: "fk_working_schedules_barber_salons_barber_salon_id",
                        column: x => x.barber_salon_id,
                        principalTable: "barber_salons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_barber_profiles_is_active_is_verified_rating",
                table: "barber_profiles",
                columns: new[] { "is_active", "is_verified", "rating" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_barber_profiles_languages",
                table: "barber_profiles",
                column: "languages")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ux_barber_profiles_user_id",
                table: "barber_profiles",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_barber_salons_salon_id",
                table: "barber_salons",
                column: "salon_id");

            migrationBuilder.CreateIndex(
                name: "ux_barber_salons_barber_profile_id_salon_id",
                table: "barber_salons",
                columns: new[] { "barber_profile_id", "salon_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_barber_salons_primary_per_profile",
                table: "barber_salons",
                column: "barber_profile_id",
                unique: true,
                filter: "is_primary");

            migrationBuilder.CreateIndex(
                name: "ix_barber_services_hair_service_id_barber_profile_id",
                table: "barber_services",
                columns: new[] { "hair_service_id", "barber_profile_id" });

            migrationBuilder.CreateIndex(
                name: "ux_barber_services_barber_profile_id_hair_service_id",
                table: "barber_services",
                columns: new[] { "barber_profile_id", "hair_service_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_barber_specializations_specialization_id_barber_profile_id",
                table: "barber_specializations",
                columns: new[] { "specialization_id", "barber_profile_id" });

            migrationBuilder.CreateIndex(
                name: "ux_barber_specializations_barber_profile_id_specialization_id",
                table: "barber_specializations",
                columns: new[] { "barber_profile_id", "specialization_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bookings_barber_profile_id_start_at_active",
                table: "bookings",
                columns: new[] { "barber_profile_id", "start_at" },
                filter: "status IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_salon_id_start_at",
                table: "bookings",
                columns: new[] { "salon_id", "start_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_user_id_start_at",
                table: "bookings",
                columns: new[] { "user_id", "start_at" });

            migrationBuilder.CreateIndex(
                name: "ux_bookings_user_id_idempotency_key",
                table: "bookings",
                columns: new[] { "user_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_favorite_barbers_barber_profile_id",
                table: "favorite_barbers",
                column: "barber_profile_id");

            migrationBuilder.CreateIndex(
                name: "ux_favorite_barbers_user_id_barber_profile_id",
                table: "favorite_barbers",
                columns: new[] { "user_id", "barber_profile_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hair_services_category_is_active",
                table: "hair_services",
                columns: new[] { "category", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_portfolio_photos_barber_profile_id_sort_order",
                table: "portfolio_photos",
                columns: new[] { "barber_profile_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_reviews_barber_profile_id_created_at",
                table: "reviews",
                columns: new[] { "barber_profile_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_reviews_booking_id",
                table: "reviews",
                column: "booking_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_salons_city_is_active",
                table: "salons",
                columns: new[] { "city", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ux_specializations_name",
                table: "specializations",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_time_offs_barber_profile_id_start_at",
                table: "time_offs",
                columns: new[] { "barber_profile_id", "start_at" });

            migrationBuilder.CreateIndex(
                name: "ix_users_barber_profile_id",
                table: "users",
                column: "barber_profile_id");

            migrationBuilder.CreateIndex(
                name: "ux_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_working_schedules_barber_salon_id_day_of_week",
                table: "working_schedules",
                columns: new[] { "barber_salon_id", "day_of_week" },
                unique: true);

            // Двойная бронь у одного барбера запрещена на уровне БД: EF Core exclusion-ограничения
            // не выражает, поэтому оно добавлено сырым SQL (см. BookingConfiguration).
            // Диапазон полуоткрытый [start, end), поэтому касание границ (EndAt == StartAt
            // следующей брони) конфликтом не считается — как в домене.
            // Активные статусы: 1 = Pending, 2 = Confirmed.
            migrationBuilder.Sql(
                """
                ALTER TABLE bookings
                    ADD CONSTRAINT ex_bookings_no_overlap
                    EXCLUDE USING gist (
                        barber_profile_id WITH =,
                        tstzrange(start_at, end_at) WITH &&
                    )
                    WHERE (status IN (1, 2));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Ограничение снимается до удаления таблицы — ради явности отката
            // (DropTable убрал бы его и сам).
            migrationBuilder.Sql(
                "ALTER TABLE bookings DROP CONSTRAINT IF EXISTS ex_bookings_no_overlap;");

            migrationBuilder.DropTable(
                name: "barber_services");

            migrationBuilder.DropTable(
                name: "barber_specializations");

            migrationBuilder.DropTable(
                name: "bookings");

            migrationBuilder.DropTable(
                name: "favorite_barbers");

            migrationBuilder.DropTable(
                name: "hair_services");

            migrationBuilder.DropTable(
                name: "portfolio_photos");

            migrationBuilder.DropTable(
                name: "reviews");

            migrationBuilder.DropTable(
                name: "salons");

            migrationBuilder.DropTable(
                name: "specializations");

            migrationBuilder.DropTable(
                name: "time_offs");

            migrationBuilder.DropTable(
                name: "working_schedules");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "barber_salons");

            migrationBuilder.DropTable(
                name: "barber_profiles");
        }
    }
}

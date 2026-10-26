using BarberSearch.Domain;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Domain.BarberProfiles.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarberSearch.Infrastructure.Postgresql.Configurations;

/// <summary>
/// Конфигурация таблицы <c>barber_profiles</c> — агрегатного корня профиля барбера.
/// </summary>
/// <remarks>
/// Объекты-значения <see cref="DisplayName"/> и <see cref="YearsOfExperience"/> — owned-типы
/// в этой же таблице: отдельного смысла вне профиля они не имеют.
/// Дочерние коллекции (салоны, услуги, специализации, портфолио, отпуска) настраиваются
/// из конфигураций самих дочерних сущностей.
/// </remarks>
public class BarberProfileConfiguration : IEntityTypeConfiguration<BarberProfile>
{
    public void Configure(EntityTypeBuilder<BarberProfile> builder)
    {
        builder.ToTable("barber_profiles", table =>
        {
            table.HasCheckConstraint(
                "ck_barber_profiles_display_name",
                FormattableString.Invariant(
                    $"char_length(display_name) BETWEEN 1 AND {DisplayName.MAX_LENGTH}"));
            table.HasCheckConstraint(
                "ck_barber_profiles_years_of_experience",
                FormattableString.Invariant(
                    $"years_of_experience BETWEEN {Constants.MIN_YEARS_OF_EXPERIENCE} AND {Constants.MAX_YEARS_OF_EXPERIENCE}"));
            table.HasCheckConstraint(
                "ck_barber_profiles_grade",
                CheckConstraintSql.EnumRange<Grade>("grade"));

            // null-рейтинг и нулевой счётчик отзывов — одно и то же состояние («отзывов нет»),
            // поэтому они меняются только вместе.
            table.HasCheckConstraint(
                "ck_barber_profiles_rating",
                "(rating IS NULL) = (reviews_count = 0) " +
                "AND (rating IS NULL OR " +
                CheckConstraintSql.DecimalRange("rating", Constants.MIN_RATING, Constants.MAX_RATING) +
                ")");
            table.HasCheckConstraint(
                "ck_barber_profiles_reviews_count",
                "reviews_count >= 0");
        });

        builder.HasKey(x => x.Id).HasName("pk_barber_profiles");

        // Оптимистическая блокировка: конкурентный UPDATE не перезаписывает чужую правку (R-05).
        builder.UseXminConcurrencyToken();

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.Grade).HasColumnName("grade").HasConversion<int>();
        builder.Property(x => x.Bio).HasColumnName("bio");
        builder.Property(x => x.Rating).HasColumnName("rating")
            .HasPrecision(ColumnPrecision.RATING_PRECISION, ColumnPrecision.RATING_SCALE);
        builder.Property(x => x.ReviewsCount).HasColumnName("reviews_count");
        builder.Property(x => x.LatePolicy).HasColumnName("late_policy").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active");
        builder.Property(x => x.IsVerified).HasColumnName("is_verified");

        builder.OwnsOne(x => x.DisplayName, displayName =>
            displayName.Property(value => value.Value)
                .HasColumnName("display_name")
                .IsRequired());

        builder.OwnsOne(x => x.YearsOfExperience, experience =>
            experience.Property(value => value.Value)
                .HasColumnName("years_of_experience")
                .IsRequired());

        // Списки строк и перечислений хранятся массивами Postgres: это компактно и
        // не требует отдельных таблиц, а фильтровать их позволяет GIN-индекс ниже.
        builder.PrimitiveCollection(x => x.Languages)
            .HasColumnName("languages")
            .IsRequired();

        builder.PrimitiveCollection(x => x.PaymentMethods)
            .HasColumnName("payment_methods")
            .IsRequired();

        builder.Property(x => x.CertificatesAndTraining)
            .HasColumnName("certificates_and_training")
            .IsRequired();

        builder.Property(x => x.Achievements)
            .HasColumnName("achievements")
            .IsRequired();

        builder.Property(x => x.CommunicationFormat)
            .HasColumnName("communication_format")
            .IsRequired();

        builder.Property(x => x.Goodies)
            .HasColumnName("goodies")
            .IsRequired();

        // У пользователя не может быть двух профилей барбера.
        builder.HasIndex(x => x.UserId)
            .IsUnique()
            .HasDatabaseName("ux_barber_profiles_user_id");

        // Индекс порядка выдачи поиска сознательно не объявлен в EF-модели: ORDER BY
        // (rating IS NULL), rating DESC, id требует expression-индекса, а HasIndex его
        // не выражает. Единственный поисковый индекс — ix_barber_profiles_search_keyset
        // (raw SQL в AddSearchKeysetIndex); прежние ix_barber_profiles_search
        // (rating DESC NULLS LAST, id) и ix_barber_profiles_is_active_is_verified_rating
        // (is_active, is_verified, rating DESC) удалены как неиспользуемые (R-38).

        // Поиск «барберы, говорящие на языке» идёт оператором @> по массиву.
        builder.HasIndex(x => x.Languages)
            .HasMethod("gin")
            .HasDatabaseName("ix_barber_profiles_languages");
    }
}

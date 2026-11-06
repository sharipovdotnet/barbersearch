using BarberSearch.Domain;
using BarberSearch.Domain.Salons;
using BarberSearch.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarberSearch.Infrastructure.Postgresql.Configurations;

/// <summary>
/// Конфигурация таблицы <c>salons</c> — агрегатного корня салона.
/// </summary>
public class SalonConfiguration : IEntityTypeConfiguration<Salon>
{
    public void Configure(EntityTypeBuilder<Salon> builder)
    {
        builder.ToTable("salons", table =>
        {
            table.HasCheckConstraint(
                "ck_salons_name",
                "char_length(btrim(name)) > 0");
            table.HasCheckConstraint(
                "ck_salons_address",
                "char_length(btrim(address)) > 0");
            table.HasCheckConstraint(
                "ck_salons_city",
                "char_length(btrim(city)) > 0");
            table.HasCheckConstraint(
                "ck_salons_country",
                "char_length(btrim(country)) > 0");
            table.HasCheckConstraint(
                "ck_salons_time_zone_id",
                "char_length(btrim(time_zone_id)) > 0");
            table.HasCheckConstraint(
                "ck_salons_latitude",
                FormattableString.Invariant(
                    $"latitude BETWEEN {Constants.MIN_LATITUDE} AND {Constants.MAX_LATITUDE}"));
            table.HasCheckConstraint(
                "ck_salons_longitude",
                FormattableString.Invariant(
                    $"longitude BETWEEN {Constants.MIN_LONGITUDE} AND {Constants.MAX_LONGITUDE}"));
            // Модерация: те же правила, что у заявки в барберы, — причина отказа не пустая
            // и не длиннее 500 символов, статус — одно из трёх значений.
            table.HasCheckConstraint(
                "ck_salons_moderation_status",
                CheckConstraintSql.EnumIn<SalonModerationStatus>("moderation_status"));
            table.HasCheckConstraint(
                "ck_salons_rejection_reason",
                FormattableString.Invariant(
                    $"rejection_reason IS NULL OR char_length(rejection_reason) BETWEEN 1 AND {Constants.MAX_SALON_REJECTION_REASON_LENGTH}"));
        });

        builder.HasKey(x => x.Id).HasName("pk_salons");

        // Оптимистическая блокировка: конкурентный UPDATE не перезаписывает чужую правку (R-05).
        builder.UseXminConcurrencyToken();

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.Property(x => x.Name).HasColumnName("name").IsRequired();
        builder.Property(x => x.Address).HasColumnName("address").IsRequired();
        builder.Property(x => x.City).HasColumnName("city").IsRequired();
        builder.Property(x => x.PostalCode).HasColumnName("postal_code");
        builder.Property(x => x.Country).HasColumnName("country").IsRequired();

        // Идентификатор часового пояса IANA, например Europe/Moscow. Разрешимость
        // проверяет домен при записи (Salon.Create / UpdateLocation, R-01).
        builder.Property(x => x.TimeZoneId).HasColumnName("time_zone_id").IsRequired();

        builder.Property(x => x.Latitude).HasColumnName("latitude");
        builder.Property(x => x.Longitude).HasColumnName("longitude");
        builder.Property(x => x.PhoneNumber).HasColumnName("phone_number");
        builder.Property(x => x.WebsiteUrl)
            .HasColumnName("website_url")
            .HasMaxLength(Constants.MAX_URL_LENGTH);
        builder.Property(x => x.IsActive).HasColumnName("is_active");

        // Владелец — связь, а не роль: у админского салона owner_user_id = NULL.
        // Удаление аккаунта владельца не удаляет салон, а обнуляет владельца.
        builder.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
        builder.Property(x => x.ModerationStatus)
            .HasColumnName("moderation_status")
            .HasConversion<int>()
            .HasDefaultValue(SalonModerationStatus.Active);
        builder.Property(x => x.RejectionReason)
            .HasColumnName("rejection_reason")
            .HasMaxLength(Constants.MAX_SALON_REJECTION_REASON_LENGTH);
        builder.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");
        builder.Property(x => x.ReviewedByUserId).HasColumnName("reviewed_by_user_id");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.OwnerUserId)
            .HasConstraintName("fk_salons_users_owner_user_id")
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.ReviewedByUserId)
            .HasConstraintName("fk_salons_users_reviewed_by_user_id")
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => new { x.City, x.IsActive })
            .HasDatabaseName("ix_salons_city_is_active");

        // Индексы под FK: удаление аккаунта владельца/администратора проверяет ссылки.
        builder.HasIndex(x => x.OwnerUserId)
            .HasDatabaseName("ix_salons_owner_user_id");
        builder.HasIndex(x => x.ReviewedByUserId)
            .HasDatabaseName("ix_salons_reviewed_by_user_id");
    }
}

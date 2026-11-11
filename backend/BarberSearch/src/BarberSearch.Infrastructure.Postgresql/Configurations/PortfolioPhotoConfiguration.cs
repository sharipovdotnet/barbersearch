using BarberSearch.Domain;
using BarberSearch.Domain.Common;
using BarberSearch.Domain.PortfolioPhotos;
using BarberSearch.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarberSearch.Infrastructure.Postgresql.Configurations;

/// <summary>
/// Конфигурация таблицы <c>portfolio_photos</c> — дочерней сущности агрегата барбера.
/// </summary>
public class PortfolioPhotoConfiguration : IEntityTypeConfiguration<PortfolioPhoto>
{
    public void Configure(EntityTypeBuilder<PortfolioPhoto> builder)
    {
        builder.ToTable("portfolio_photos", table =>
        {
            table.HasCheckConstraint(
                "ck_portfolio_photos_url",
                "char_length(btrim(url)) > 0");
            table.HasCheckConstraint(
                "ck_portfolio_photos_sort_order",
                "sort_order >= 0");

            // Премодерация: статус — одно из трёх значений, причина отказа не пустая и
            // не длиннее 500 символов. Те же правила проверяет домен, ограничение —
            // последняя линия защиты.
            table.HasCheckConstraint(
                "ck_portfolio_photos_moderation_status",
                CheckConstraintSql.EnumIn<PhotoModerationStatus>("moderation_status"));
            table.HasCheckConstraint(
                "ck_portfolio_photos_rejection_reason",
                FormattableString.Invariant(
                    $"rejection_reason IS NULL OR char_length(rejection_reason) BETWEEN 1 AND {Constants.MAX_PHOTO_REJECTION_REASON_LENGTH}"));
        });

        builder.HasKey(x => x.Id).HasName("pk_portfolio_photos");

        // xmin и на дочерней строке: UPDATE ребёнка не трогает корень агрегата (R-05).
        builder.UseXminConcurrencyToken();

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.Property(x => x.BarberProfileId).HasColumnName("barber_profile_id");

        // Длины повторяют доменные пределы (R-24): varchar отсекает строку в мегабайт,
        // которую иначе можно было бы записать в text в обход API.
        builder.Property(x => x.Url)
            .HasColumnName("url")
            .HasMaxLength(Constants.MAX_URL_LENGTH)
            .IsRequired();
        builder.Property(x => x.ThumbnailUrl)
            .HasColumnName("thumbnail_url")
            .HasMaxLength(Constants.MAX_URL_LENGTH);
        builder.Property(x => x.Description)
            .HasColumnName("description")
            .HasMaxLength(Constants.MAX_TEXT_LENGTH);
        builder.Property(x => x.SortOrder).HasColumnName("sort_order");
        builder.Property(x => x.IsPublished).HasColumnName("is_published");

        // Административная блокировка: отдельное от is_published состояние. NOT NULL
        // с DEFAULT false — миграция AddPortfolioPhotoBlocked; существующие строки
        // остаются видимыми.
        builder.Property(x => x.IsBlocked)
            .HasColumnName("is_blocked")
            .IsRequired();

        // Премодерация: enum хранится числом в smallint (значения стабильны). Default 2
        // (Approved) описывает все существующие строки — миграция AddPhotoModeration
        // не перепроверяет старые кадры.
        builder.Property(x => x.ModerationStatus)
            .HasColumnName("moderation_status")
            .HasConversion<short>()
            .HasDefaultValue(PhotoModerationStatus.Approved);
        builder.Property(x => x.RejectionReason)
            .HasColumnName("rejection_reason")
            .HasMaxLength(Constants.MAX_PHOTO_REJECTION_REASON_LENGTH);
        builder.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");
        builder.Property(x => x.ReviewedByUserId).HasColumnName("reviewed_by_user_id");

        // Кто проверил кадр: удаление аккаунта администратора не удаляет фотографию,
        // а обнуляет автора решения.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.ReviewedByUserId)
            .HasConstraintName("fk_portfolio_photos_users_reviewed_by_user_id")
            .OnDelete(DeleteBehavior.SetNull);

        // Ребёнок живёт только вместе с агрегатом.
        builder.HasOne(x => x.BarberProfile)
            .WithMany(profile => profile.PortfolioPhotos)
            .HasForeignKey(x => x.BarberProfileId)
            .HasConstraintName("fk_portfolio_photos_barber_profiles_barber_profile_id")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.BarberProfileId, x.SortOrder })
            .HasDatabaseName("ix_portfolio_photos_barber_profile_id_sort_order");

        // Очередь модерации: только Pending, FIFO по created_at, id — хвост курсора.
        builder.HasIndex(x => new { x.ModerationStatus, x.CreatedAt, x.Id })
            .HasDatabaseName("ix_portfolio_photos_moderation_status_created_at");

        // Индекс под FK: удаление аккаунта администратора проверяет ссылки.
        builder.HasIndex(x => x.ReviewedByUserId)
            .HasDatabaseName("ix_portfolio_photos_reviewed_by_user_id");
    }
}

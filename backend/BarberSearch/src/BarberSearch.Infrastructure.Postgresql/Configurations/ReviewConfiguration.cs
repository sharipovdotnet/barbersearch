using BarberSearch.Domain;
using BarberSearch.Domain.Reviews;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarberSearch.Infrastructure.Postgresql.Configurations;

/// <summary>
/// Конфигурация таблицы <c>reviews</c> — агрегатного корня отзыва.
/// </summary>
public class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("reviews", table =>
        {
            table.HasCheckConstraint(
                "ck_reviews_rating",
                FormattableString.Invariant(
                    $"rating BETWEEN {Constants.MIN_REVIEW_RATING} AND {Constants.MAX_REVIEW_RATING}"));
            table.HasCheckConstraint(
                "ck_reviews_text",
                FormattableString.Invariant(
                    $"text IS NULL OR char_length(text) <= {Constants.MAX_TEXT_LENGTH}"));
        });

        builder.HasKey(x => x.Id).HasName("pk_reviews");

        // Оптимистическая блокировка: конкурентный UPDATE не перезаписывает чужую правку (R-05).
        builder.UseXminConcurrencyToken();

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.Property(x => x.BookingId).HasColumnName("booking_id");
        builder.Property(x => x.BarberProfileId).HasColumnName("barber_profile_id");
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.Rating).HasColumnName("rating");
        builder.Property(x => x.Text).HasColumnName("text");
        builder.Property(x => x.IsPublished).HasColumnName("is_published");

        // У одной брони может быть только один отзыв: правило домен не выражает.
        builder.HasIndex(x => x.BookingId)
            .IsUnique()
            .HasDatabaseName("ux_reviews_booking_id");

        // Отзывы барбера для его карточки: свежие сверху.
        builder.HasIndex(x => new { x.BarberProfileId, x.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_reviews_barber_profile_id_created_at");

        // Очередь модерации: скрытые отзывы всех барберов, старые сверху (ASC-скан).
        // Частичный индекс: опубликованные строки в него не попадают (R-35).
        builder.HasIndex(x => new { x.CreatedAt, x.Id })
            .HasFilter("is_published = false")
            .HasDatabaseName("ix_reviews_moderation_created_at_id");
    }
}

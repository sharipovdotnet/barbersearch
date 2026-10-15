using BarberSearch.Domain.PortfolioPhotos;
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
        });

        builder.HasKey(x => x.Id).HasName("pk_portfolio_photos");

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.Property(x => x.BarberProfileId).HasColumnName("barber_profile_id");
        builder.Property(x => x.Url).HasColumnName("url").IsRequired();
        builder.Property(x => x.ThumbnailUrl).HasColumnName("thumbnail_url");
        builder.Property(x => x.Description).HasColumnName("description");
        builder.Property(x => x.SortOrder).HasColumnName("sort_order");
        builder.Property(x => x.IsPublished).HasColumnName("is_published");

        // Ребёнок живёт только вместе с агрегатом.
        builder.HasOne(x => x.BarberProfile)
            .WithMany(profile => profile.PortfolioPhotos)
            .HasForeignKey(x => x.BarberProfileId)
            .HasConstraintName("fk_portfolio_photos_barber_profiles_barber_profile_id")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.BarberProfileId, x.SortOrder })
            .HasDatabaseName("ix_portfolio_photos_barber_profile_id_sort_order");
    }
}

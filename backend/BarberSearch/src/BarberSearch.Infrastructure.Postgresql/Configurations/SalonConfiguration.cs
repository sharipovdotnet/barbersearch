using BarberSearch.Domain;
using BarberSearch.Domain.Salons;
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
        });

        builder.HasKey(x => x.Id).HasName("pk_salons");

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.Property(x => x.Name).HasColumnName("name").IsRequired();
        builder.Property(x => x.Address).HasColumnName("address").IsRequired();
        builder.Property(x => x.City).HasColumnName("city").IsRequired();
        builder.Property(x => x.PostalCode).HasColumnName("postal_code");
        builder.Property(x => x.Country).HasColumnName("country").IsRequired();

        // Идентификатор часового пояса IANA, например Europe/Moscow.
        builder.Property(x => x.TimeZoneId).HasColumnName("time_zone_id").IsRequired();

        builder.Property(x => x.Latitude).HasColumnName("latitude");
        builder.Property(x => x.Longitude).HasColumnName("longitude");
        builder.Property(x => x.PhoneNumber).HasColumnName("phone_number");
        builder.Property(x => x.WebsiteUrl).HasColumnName("website_url");
        builder.Property(x => x.IsActive).HasColumnName("is_active");

        builder.HasIndex(x => new { x.City, x.IsActive })
            .HasDatabaseName("ix_salons_city_is_active");
    }
}

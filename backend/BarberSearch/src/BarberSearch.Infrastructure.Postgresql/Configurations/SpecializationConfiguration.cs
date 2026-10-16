using BarberSearch.Domain.Specializations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarberSearch.Infrastructure.Postgresql.Configurations;

/// <summary>
/// Конфигурация таблицы <c>specializations</c> — справочника специализаций.
/// </summary>
public class SpecializationConfiguration : IEntityTypeConfiguration<Specialization>
{
    public void Configure(EntityTypeBuilder<Specialization> builder)
    {
        builder.ToTable("specializations", table =>
            table.HasCheckConstraint(
                "ck_specializations_name",
                "char_length(btrim(name)) > 0"));

        builder.HasKey(x => x.Id).HasName("pk_specializations");

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        // citext: сравнение без учёта регистра делает сама БД, поэтому обычный уникальный
        // индекс ниже запрещает и «Стрижка», и «стрижка» (решение владельца, AGENT.md §12).
        builder.Property(x => x.Name).HasColumnName("name").HasColumnType("citext").IsRequired();
        builder.Property(x => x.Description).HasColumnName("description");
        builder.Property(x => x.IsActive).HasColumnName("is_active");

        // Имя занимает и неактивная запись: иначе после её активации в справочнике
        // оказались бы два одинаковых тега, и фильтр клиента разошёлся бы на два.
        builder.HasIndex(x => x.Name)
            .HasDatabaseName("ux_specializations_name")
            .IsUnique();
    }
}

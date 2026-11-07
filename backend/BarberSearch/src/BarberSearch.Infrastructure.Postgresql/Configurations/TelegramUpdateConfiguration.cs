using BarberSearch.Infrastructure.Postgresql.Telegram;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarberSearch.Infrastructure.Postgresql.Configurations;

/// <summary>
/// Конфигурация таблицы <c>telegram_updates</c> — журнала идемпотентности вебхука Telegram.
/// </summary>
/// <remarks>
/// Первичный ключ <c>update_id</c> и есть уникальность: отдельный индекс не нужен.
/// Момент приёма <c>received_at</c> хранится для диагностики и будущей уборки журнала.
/// </remarks>
public sealed class TelegramUpdateConfiguration : IEntityTypeConfiguration<TelegramUpdate>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TelegramUpdate> builder)
    {
        builder.ToTable("telegram_updates");

        builder.HasKey(x => x.UpdateId).HasName("pk_telegram_updates");

        builder.Property(x => x.UpdateId)
            .HasColumnName("update_id")
            .ValueGeneratedNever();
        builder.Property(x => x.ReceivedAt).HasColumnName("received_at");
    }
}

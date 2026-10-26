using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarberSearch.Infrastructure.Postgresql.Configurations;

/// <summary>
/// Настраивает оптимистическую блокировку агрегата через системную колонку PostgreSQL
/// <c>xmin</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>xmin</c> — служебная колонка таблицы: PostgreSQL меняет её при каждом <c>UPDATE</c>
/// строки, поэтому она работает как бесплатный <c>rowversion</c>. EF Core читает её как
/// теневую <c>uint</c>-свойство и добавляет в <c>WHERE</c> условия <c>xmin = @original</c>.
/// Если строку успел изменить другой запрос, affected rows = 0, и <c>SaveChanges</c>
/// бросает <c>DbUpdateConcurrencyException</c> вместо молчаливой перезаписи чужой правки.
/// </para>
/// <para>
/// В Npgsql 10 отдельного <c>UseXminAsConcurrencyToken</c> больше нет: провайдер распознаёт
/// <c>uint</c>-свойство с <c>IsRowVersion</c> и сам привязывает его к <c>xmin</c>
/// (<c>NpgsqlPostgresModelFinalizingConvention.ProcessRowVersionProperty</c>). Схема БД при
/// этом не меняется: колонка системная, миграции её не создают.
/// </para>
/// </remarks>
internal static class ConcurrencyTokenExtensions
{
    /// <summary>
    /// Добавляет в модель теневое свойство <c>xmin</c> и делает его токеном конкурентности.
    /// </summary>
    public static EntityTypeBuilder<TEntity> UseXminConcurrencyToken<TEntity>(
        this EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property<uint>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .IsRowVersion();

        return builder;
    }
}

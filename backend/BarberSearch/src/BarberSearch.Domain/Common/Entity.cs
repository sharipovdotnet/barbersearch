using System;

namespace BarberSearch.Domain.Common;

public class Entity
{
    /// <summary>
    /// Первичный ключ. Генерируется клиентом как UUIDv7 (time-ordered), чтобы вставки
    /// шли в порядке btree и не вызывали расщепление страниц индекса.
    /// </summary>
    public Guid Id { get; private set; }

    public DateTimeOffset CreatedAt { get; protected set; }
    public DateTimeOffset UpdatedAt { get; protected set; }

    protected Entity()
    {
        var now = DateTimeOffset.UtcNow;

        Id = Guid.CreateVersion7();
        CreatedAt = now;
        UpdatedAt = now;
    }

    protected void MarkUpdated()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Сравнение по идентификатору. Реализовано без <c>GetType()</c>, чтобы прокси
    /// EF Core (lazy-loading / change-tracking) оставались равны исходной сущности.
    /// </summary>
    public bool Equals(Entity? other)
    {
        if (other is null)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        if (Id == Guid.Empty || other.Id == Guid.Empty)
            return false;

        return Id == other.Id;
    }

    public override bool Equals(object? obj)
        => obj is Entity entity && Equals(entity);

    public override int GetHashCode()
        => Id.GetHashCode();

    public static bool operator ==(Entity? left, Entity? right)
        => left?.Equals(right) ?? right is null;

    public static bool operator !=(Entity? left, Entity? right)
        => !(left == right);
}

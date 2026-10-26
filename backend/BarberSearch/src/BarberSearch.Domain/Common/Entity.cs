namespace BarberSearch.Domain.Common;

/// <summary>
/// Базовая сущность: идентификатор и технические метки времени.
/// </summary>
/// <remarks>
/// <para>
/// Политика часов (R-27, <c>AGENT.md</c> §7.6): <see cref="CreatedAt"/> и <see cref="UpdatedAt"/>
/// — технические метки аудита, а не бизнес-«сейчас». Домен ставит их системными часами
/// (<see cref="DateTimeOffset.UtcNow"/>) сознательно и не принимает <c>TimeProvider</c>:
/// ни одно доменное правило не зависит от этих значений, поэтому подмена часов в тестах
/// не нужна. Бизнес-«сейчас» (окно брони, «не раньше начала», сроки refresh-токенов)
/// решают Application и Web через <c>TimeProvider</c>.
/// </para>
/// <para>
/// Менять это поведение (передавать часы в агрегат) — отдельный срез: тогда решение
/// о времени станет частью доменного API и всех фабрик.
/// </para>
/// <para>
/// Равенство (R-41): сущности сравниваются только по <see cref="Id"/> и <b>без</b>
/// <c>GetType()</c>. Это осознанно: EF Core создаёт прокси — подтипы исходного типа
/// (lazy-loading / change-tracking), — и сравнение по типу развалило бы равенство прокси
/// и оригинала. Хеш тоже считается только по <see cref="Id"/>, поэтому прокси и оригинал
/// попадают в один сегмент словаря.
/// </para>
/// <para>
/// Класс реализует <c>IEquatable&lt;Entity&gt;</c>: <see cref="Equals(Entity)"/> был и раньше,
/// но без объявленного контракта. Типизированный вызов через интерфейс (и
/// <c>EqualityComparer&lt;Entity&gt;.Default</c>) идёт напрямую в этот метод — без приведения
/// к <c>object</c> и без проверки <c>is Entity</c> из <see cref="Equals(object)"/>
/// (для значимых типов такой контракт — ещё и сравнение без боксинга). Поведение от этого
/// не меняется: обе перегрузки и операторы <c>==</c>/<c>!=</c> дают один и тот же ответ.
/// </para>
/// <para>
/// <see cref="Guid.Empty"/> не считается идентификатором: две <b>разные</b> сущности
/// с пустым <see cref="Id"/> не равны друг другу, иначе все объекты, ещё не получившие
/// ключ, схлопнулись бы в один. Совпадение экземпляра с самим собой отсекается раньше,
/// по <c>ReferenceEquals</c>, поэтому сущность с пустым <see cref="Id"/> равна только себе.
/// </para>
/// </remarks>
public class Entity : IEquatable<Entity>
{
    /// <summary>
    /// Первичный ключ. Генерируется клиентом как UUIDv7 (time-ordered), чтобы вставки
    /// шли в порядке btree и не вызывали расщепление страниц индекса.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Техническая метка создания — системные часы, не бизнес-«сейчас» (см. примечание класса).
    /// </summary>
    public DateTimeOffset CreatedAt { get; protected set; }

    /// <summary>
    /// Техническая метка последнего изменения — системные часы, не бизнес-«сейчас».
    /// </summary>
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
    /// Реализует <c>IEquatable&lt;Entity&gt;.Equals(Entity?)</c>: типизированный вызов
    /// (в том числе через интерфейс и <c>EqualityComparer&lt;Entity&gt;.Default</c>)
    /// приходит сюда напрямую, минуя приведение к <c>object</c> и проверку <c>is Entity</c>.
    /// </summary>
    public bool Equals(Entity? other)
    {
        if (other is null)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        // Guid.Empty — не идентификатор: разные сущности без ключа не равны друг другу.
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

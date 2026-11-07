namespace BarberSearch.IntegrationTests;

/// <summary>
/// Часы теста: «сейчас» можно переставить без ожидания и без <see cref="Thread.Sleep"/>.
/// </summary>
/// <remarks>
/// Интеграционный тест дайджеста должен попасть ровно в минуту <c>DigestTime</c> локального
/// пояса салона, а ждать до 08:00 следующего дня бессмысленно. Регистрация подменяет
/// singleton <see cref="TimeProvider"/> в DI; тики, авторизация и <c>TimeProvider</c>
/// обработчиков читают одно и то же значение.
/// </remarks>
public sealed class MutableTimeProvider : TimeProvider
{
    private long _utcTicks = DateTimeOffset.UtcNow.UtcTicks;

    /// <summary>
    /// Текущий момент UTC.
    /// </summary>
    public DateTimeOffset UtcNow
        => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);

    /// <summary>
    /// Переставляет часы.
    /// </summary>
    /// <param name="now">Новый момент; должен быть в UTC.</param>
    public void SetUtcNow(DateTimeOffset now)
        => Interlocked.Exchange(ref _utcTicks, now.UtcTicks);

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => UtcNow;
}

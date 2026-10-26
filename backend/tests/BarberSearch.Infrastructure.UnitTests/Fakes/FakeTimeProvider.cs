namespace BarberSearch.Infrastructure.UnitTests.Fakes;

/// <summary>
/// Часы, которыми управляет тест: метки JWT проверяются без ожидания и без системного времени.
/// </summary>
/// <remarks>
/// В проекте Application такой фейк уже есть; Infrastructure на него не ссылается, поэтому
/// здесь лежит собственный минимальный вариант — поведение то же самое.
/// </remarks>
public sealed class FakeTimeProvider : TimeProvider
{
    public FakeTimeProvider(DateTimeOffset utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTimeOffset UtcNow { get; set; }

    public override DateTimeOffset GetUtcNow() => UtcNow;
}

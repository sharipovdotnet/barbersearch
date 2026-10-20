namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// Часы, которыми управляет тест: срок жизни refresh-токена проверяется без ожидания.
/// </summary>
public sealed class FakeTimeProvider : TimeProvider
{
    public FakeTimeProvider(DateTimeOffset utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTimeOffset UtcNow { get; set; }

    public override DateTimeOffset GetUtcNow() => UtcNow;
}

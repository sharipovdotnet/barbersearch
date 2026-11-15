namespace BarberSearch.Mobile.Core.Localization;

/// <summary>
/// Хранилище выбранного языка.
/// </summary>
/// <remarks>
/// Core не знает про MAUI: приложение реализует хранилище через <c>Preferences</c>,
/// а тесты — в памяти. Чтение и запись не должны бросать: недоступное хранилище
/// оставляет язык в памяти до перезапуска, как в вебе с заблокированным localStorage.
/// </remarks>
public interface ILocaleStore
{
    /// <summary>
    /// Читает сохранённый язык; при ошибке или отсутствии значения — русский.
    /// </summary>
    /// <returns>Сохранённый язык.</returns>
    Locale Read();

    /// <summary>
    /// Сохраняет выбранный язык.
    /// </summary>
    /// <param name="locale">Язык интерфейса.</param>
    void Write(Locale locale);
}

/// <summary>
/// Хранилище в памяти: значение по умолчанию для тестов и дизайнера.
/// </summary>
public sealed class InMemoryLocaleStore : ILocaleStore
{
    private Locale _locale;

    /// <summary>
    /// Создаёт хранилище с заданным языком.
    /// </summary>
    /// <param name="initial">Начальный язык; по умолчанию русский.</param>
    public InMemoryLocaleStore(Locale initial = Locale.Ru) => _locale = initial;

    /// <inheritdoc />
    public Locale Read() => _locale;

    /// <inheritdoc />
    public void Write(Locale locale) => _locale = locale;
}

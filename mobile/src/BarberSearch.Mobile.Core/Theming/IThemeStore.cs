namespace BarberSearch.Mobile.Core.Theming;

/// <summary>
/// Хранилище выбранной темы.
/// </summary>
/// <remarks>
/// Core не знает про MAUI: приложение реализует хранилище через <c>Preferences</c>,
/// а тесты — в памяти. Чтение и запись не должны бросать: недоступное хранилище
/// оставляет тему в памяти до перезапуска, как у выбора языка при закрытом хранилище.
/// </remarks>
public interface IThemeStore
{
    /// <summary>
    /// Читает сохранённую тему; при ошибке или отсутствии значения — тема системы.
    /// </summary>
    /// <returns>Сохранённая тема.</returns>
    ThemePreference Read();

    /// <summary>
    /// Сохраняет выбранную тему.
    /// </summary>
    /// <param name="preference">Выбранная тема.</param>
    void Write(ThemePreference preference);
}

/// <summary>
/// Хранилище в памяти: значение по умолчанию для тестов и дизайнера.
/// </summary>
public sealed class InMemoryThemeStore : IThemeStore
{
    private ThemePreference _preference;

    /// <summary>
    /// Создаёт хранилище с заданной темой.
    /// </summary>
    /// <param name="initial">Начальная тема; по умолчанию тема системы.</param>
    public InMemoryThemeStore(ThemePreference initial = ThemePreference.System)
        => _preference = initial;

    /// <inheritdoc />
    public ThemePreference Read() => _preference;

    /// <inheritdoc />
    public void Write(ThemePreference preference) => _preference = preference;
}

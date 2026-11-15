namespace BarberSearch.Mobile.Core.Location;

/// <summary>
/// Чем закончилась попытка прочитать геопозицию.
/// </summary>
/// <remarks>
/// Экран различает отказ в разрешении и техническую недоступность: в первом случае человек
/// может включить доступ в настройках, во втором — только попробовать позже. Без этого
/// различия подсказка была бы одинаковой и бесполезной.
/// </remarks>
public enum LocationStatus
{
    /// <summary>
    /// Координаты получены.
    /// </summary>
    Available,

    /// <summary>
    /// Пользователь не разрешил доступ к геопозиции.
    /// </summary>
    Denied,

    /// <summary>
    /// Доступ разрешён, но координаты получить не удалось: нет GPS, таймаут, ошибка платформы.
    /// </summary>
    Unavailable,
}

/// <summary>
/// Результат чтения геопозиции: координаты либо причина, по которой их нет.
/// </summary>
/// <param name="Status">Итог попытки.</param>
/// <param name="Position">Координаты; <c>null</c>, если <paramref name="Status"/> не <see cref="LocationStatus.Available"/>.</param>
public sealed record LocationResult(LocationStatus Status, GeoPosition? Position)
{
    /// <summary>
    /// Координаты получены.
    /// </summary>
    /// <param name="position">Точка устройства.</param>
    public static LocationResult Ok(GeoPosition position) => new(LocationStatus.Available, position);

    /// <summary>
    /// Доступ к геопозиции запрещён пользователем.
    /// </summary>
    public static LocationResult Denied() => new(LocationStatus.Denied, null);

    /// <summary>
    /// Координаты получить не удалось.
    /// </summary>
    public static LocationResult Unavailable() => new(LocationStatus.Unavailable, null);
}

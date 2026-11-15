namespace BarberSearch.Mobile.Core.Push;

/// <summary>
/// Состояние получения токена устройства.
/// </summary>
public enum PushTokenStatus
{
    /// <summary>
    /// Токен получен; только в этом состоянии есть что регистрировать на сервере.
    /// </summary>
    Available,

    /// <summary>
    /// Токена нет по причине устройства: нет сервисов Google Play или FCM не выдал токен.
    /// Конфигурация при этом есть, и повторять попытку на этом телефоне бесполезно.
    /// </summary>
    Unavailable,

    /// <summary>
    /// Регистрация не удалась по временной причине: нет сети, <c>401/5xx</c>, таймаут или
    /// отмена. Это не свойство устройства, поэтому экран не показывает подсказку про
    /// настройки телефона — следующая попытка (возврат в приложение, вход) может успеть.
    /// </summary>
    TransientFailure,

    /// <summary>
    /// Человек не дал разрешение на уведомления; повторно спрашивать бессмысленно, пока
    /// он не включит их в настройках телефона.
    /// </summary>
    Denied,

    /// <summary>
    /// Канал выключен: нет <c>push.json</c>, выключен переключатель или нет сессии.
    /// </summary>
    Disabled,
}

/// <summary>
/// Результат получения токена FCM: состояние и сам токен, если он есть.
/// </summary>
/// <remarks>
/// Тип нужен, чтобы разделить «не настроено», «человек запретил», «устройство не умеет» и
/// «временный сбой сети»: экран «Профиля» показывает подсказку только для запрещённого
/// разрешения и для устройства без push, а временный сбой молча ждёт следующей попытки.
/// </remarks>
public sealed record PushTokenResult
{
    private PushTokenResult(PushTokenStatus status, string? token)
    {
        Status = status;
        Token = token;
    }

    /// <summary>
    /// Токена нет по причине устройства.
    /// </summary>
    public static PushTokenResult Unavailable { get; } = new(PushTokenStatus.Unavailable, null);

    /// <summary>
    /// Регистрация сорвалась по временной причине; метку токена это не описывает.
    /// </summary>
    public static PushTokenResult TransientFailure { get; } =
        new(PushTokenStatus.TransientFailure, null);

    /// <summary>
    /// Разрешение на уведомления не выдано.
    /// </summary>
    public static PushTokenResult Denied { get; } = new(PushTokenStatus.Denied, null);

    /// <summary>
    /// Push не настроен или выключен.
    /// </summary>
    public static PushTokenResult Disabled { get; } = new(PushTokenStatus.Disabled, null);

    /// <summary>
    /// Текущее состояние.
    /// </summary>
    public PushTokenStatus Status { get; }

    /// <summary>
    /// Токен FCM; <c>null</c> для всех состояний, кроме <see cref="PushTokenStatus.Available"/>.
    /// </summary>
    public string? Token { get; }

    /// <summary>
    /// Есть ли токен, который можно отправить на сервер.
    /// </summary>
    public bool IsAvailable => Status == PushTokenStatus.Available && !string.IsNullOrEmpty(Token);

    /// <summary>
    /// Создаёт результат с токеном.
    /// </summary>
    /// <param name="token">Непустой токен FCM.</param>
    /// <returns>Результат <see cref="PushTokenStatus.Available"/>.</returns>
    public static PushTokenResult Available(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        return new PushTokenResult(PushTokenStatus.Available, token);
    }
}

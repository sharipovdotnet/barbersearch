namespace BarberSearch.Domain.Push;

/// <summary>
/// Платформа устройства, на которое доставляется push-уведомление.
/// </summary>
/// <remarks>
/// <para>
/// В этом срезе реально работает только <see cref="Android"/>: транспорт — FCM HTTP v1.
/// <see cref="Ios"/> зарезервирован заранее, потому что токен устройства и таблица
/// <c>push_device_tokens</c> общие для обеих платформ, а появление APNs не должно требовать
/// миграции схемы: добавится только отправитель и разрешённое значение.
/// </para>
/// <para>
/// Значения числовые и явные (AGENT.md §10.10); в JSON и в API платформа передаётся именем
/// (<c>"Android"</c>), а не числом — так же, как остальные перечисления репозитория.
/// </para>
/// </remarks>
public enum PushPlatform
{
    /// <summary>
    /// Android: доставка через Firebase Cloud Messaging.
    /// </summary>
    Android = 1,

    /// <summary>
    /// iOS: зарезервировано под APNs, в текущем срезе не обслуживается.
    /// </summary>
    Ios = 2
}

using BarberSearch.Domain.Push;

namespace BarberSearch.Contracts.Push.RegisterPushDevice;

/// <summary>
/// Данные запроса на регистрацию устройства для push-уведомлений.
/// </summary>
/// <remarks>
/// Токен выдаёт FCM на устройстве, и сервер не проверяет его заранее: валидность выясняется
/// первой отправкой (<c>UNREGISTERED</c> или <c>INVALID_ARGUMENT</c> — токен удаляется).
/// Регистрация идемпотентна: повторный <c>POST</c> с тем же токеном обновляет <c>last_seen_at</c>,
/// а токен, зарегистрированный на этом устройстве другим аккаунтом, переходит к текущему
/// пользователю — иначе напоминание уходило бы прежнему владельцу телефона.
/// </remarks>
/// <param name="Token">Токен устройства FCM; непустой, не длиннее 512 символов.</param>
/// <param name="Platform">
/// Платформа устройства; в этом срезе обслуживается только <see cref="PushPlatform.Android"/>.
/// </param>
public record RegisterPushDeviceRequest(string Token, PushPlatform Platform);

using System.Text.Json;

namespace BarberSearch.Mobile.Core.Push;

/// <summary>
/// Настройки канала push, прочитанные из ресурса приложения <c>push.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// Конфигурация Firebase у каждого стенда своя и содержит ключ проекта, поэтому в репозитории
/// лежит только шаблон (<c>push.example.json</c>), а сам <c>push.json</c> игнорируется git.
/// Пока файла нет, <see cref="Disabled"/> — это и есть «push не настроен»: приложение
/// собирается и работает без единого нового разрешения и без строки настроек в «Профиле».
/// </para>
/// <para>
/// Разбор — в Core, а не в платформенном коде: правило «нет файла / битый JSON / <c>enabled</c>
/// не <c>true</c> / пустое поле → канал выключен» проверяется тестами без эмулятора, а Android
/// только читает ресурс и строит из готовых значений <c>FirebaseOptions</c>.
/// </para>
/// <para>
/// Поля необязательны по смыслу, но не по типу: у <see cref="Disabled"/> они пустые строки,
/// а не <c>null</c>, чтобы платформенный код не проверял их ещё раз перед передачей в
/// <c>FirebaseOptions</c>. Пустая строка при <c>Enabled: false</c> никуда не уходит.
/// </para>
/// </remarks>
/// <param name="Enabled">Включён ли канал; всё остальное имеет смысл только при <c>true</c>.</param>
/// <param name="ProjectId">Идентификатор проекта Firebase (<c>project_info.project_id</c>).</param>
/// <param name="ApplicationId">Идентификатор приложения Android (<c>mobilesdk_app_id</c>).</param>
/// <param name="ApiKey">Ключ веб-API Firebase (<c>api_key[].current_key</c>).</param>
/// <param name="SenderId">Номер отправителя GCM/FCM (<c>project_info.gcm_defaultSenderId</c>).</param>
public sealed record PushOptions(
    bool Enabled,
    string ProjectId,
    string ApplicationId,
    string ApiKey,
    string SenderId)
{
    /// <summary>
    /// Канал не настроен: ни Firebase, ни разрешений, ни строки в «Профиле».
    /// </summary>
    public static PushOptions Disabled { get; } = new(
        false,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty);

    private static readonly JsonSerializerOptions ConfigJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Разбирает содержимое <c>push.json</c>.
    /// </summary>
    /// <remarks>
    /// Любая причина «не настроено» — отсутствие текста, битый JSON, <c>enabled: false</c> или
    /// пустое обязательное поле — возвращает <see cref="Disabled"/>, а не исключение: старт
    /// приложения не должен зависеть от того, положили ли рядом конфигурацию Firebase.
    /// </remarks>
    /// <param name="json">Содержимое ресурса либо <c>null</c>, если ресурса нет.</param>
    /// <returns>Настройки или <see cref="Disabled"/>.</returns>
    public static PushOptions Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Disabled;
        }

        PushConfigFile? file;

        try
        {
            file = JsonSerializer.Deserialize<PushConfigFile>(json, ConfigJson);
        }
        catch (JsonException)
        {
            return Disabled;
        }

        if (file is null
            || !file.Enabled
            || string.IsNullOrWhiteSpace(file.ProjectId)
            || string.IsNullOrWhiteSpace(file.ApplicationId)
            || string.IsNullOrWhiteSpace(file.ApiKey)
            || string.IsNullOrWhiteSpace(file.SenderId))
        {
            return Disabled;
        }

        return new PushOptions(
            true,
            file.ProjectId.Trim(),
            file.ApplicationId.Trim(),
            file.ApiKey.Trim(),
            file.SenderId.Trim());
    }

    /// <summary>
    /// Поля файла ресурса; имена — camelCase, как в шаблоне <c>push.example.json</c>.
    /// </summary>
    private sealed record PushConfigFile(
        bool Enabled,
        string? ProjectId,
        string? ApplicationId,
        string? ApiKey,
        string? SenderId);
}

using System.Globalization;
using System.Net.Http.Json;
using BarberSearch.Contracts.BarberProfiles.AddBarberProfileTimeOff;
using BarberSearch.Contracts.BarberProfiles.BarberSchedule;
using BarberSearch.Contracts.BarberSalons.ReplaceWorkingSchedules;
using BarberSearch.Contracts.BarberSalons.SetScheduleException;

namespace BarberSearch.Mobile.Core.Api.Barber;

/// <summary>
/// HTTP-клиент расписания мастера.
/// </summary>
/// <remarks>
/// Пути повторяют маршруты <c>BarberScheduleController</c> и <c>BarberProfilesController</c>:
/// чтение и замена недели живут в первом, отпуска — во втором, но для экрана это один ресурс
/// «расписание профиля». Все записи отвечают <c>204</c>, поэтому тело успешного ответа не
/// читается. <c>X-CSRF-TOKEN</c> не отправляется: клиент Bearer, а не cookie.
/// </remarks>
public sealed class BarberScheduleApi : IBarberScheduleApi
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Создаёт клиент поверх настроенного <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">
    /// Клиент с заданным <see cref="HttpClient.BaseAddress"/> и обработчиком авторизации.
    /// </param>
    public BarberScheduleApi(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<BarberScheduleResponse> GetAsync(
        Guid barberProfileId,
        DateOnly? from = null,
        DateOnly? toDate = null,
        CancellationToken cancellationToken = default)
    {
        EnsureProfileId(barberProfileId);

        using var response = await _httpClient
            .GetAsync(BuildSchedulePath(barberProfileId, from, toDate), cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var schedule = await response.Content
            .ReadFromJsonAsync<BarberScheduleResponse>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return schedule ?? throw new InvalidOperationException(
            "The API returned an empty barber schedule response.");
    }

    /// <inheritdoc />
    public async Task ReplaceWorkingSchedulesAsync(
        Guid barberProfileId,
        Guid salonId,
        ReplaceWorkingSchedulesRequest request,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        EnsureProfileId(barberProfileId);
        EnsureSalonId(salonId);
        ArgumentNullException.ThrowIfNull(request);

        var path = string.Concat(
            BuildSalonPath(barberProfileId, salonId, "working-schedules"),
            ForceQuery(force));

        using var httpRequest = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = JsonContent.Create(request, options: ApiJson.Options),
        };

        using var response = await _httpClient
            .SendAsync(httpRequest, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetScheduleExceptionAsync(
        Guid barberProfileId,
        Guid salonId,
        DateOnly exceptionDate,
        SetScheduleExceptionRequest request,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        EnsureProfileId(barberProfileId);
        EnsureSalonId(salonId);
        ArgumentNullException.ThrowIfNull(request);

        var path = string.Concat(
            BuildSalonPath(
                barberProfileId,
                salonId,
                string.Concat(
                    "schedule-exceptions/",
                    exceptionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))),
            ForceQuery(force));

        using var httpRequest = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = JsonContent.Create(request, options: ApiJson.Options),
        };

        using var response = await _httpClient
            .SendAsync(httpRequest, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveScheduleExceptionAsync(
        Guid barberProfileId,
        Guid salonId,
        DateOnly exceptionDate,
        CancellationToken cancellationToken = default)
    {
        EnsureProfileId(barberProfileId);
        EnsureSalonId(salonId);

        var path = BuildSalonPath(
            barberProfileId,
            salonId,
            string.Concat(
                "schedule-exceptions/",
                exceptionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

        using var response = await _httpClient
            .DeleteAsync(path, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AddTimeOffAsync(
        Guid barberProfileId,
        AddBarberProfileTimeOffRequest request,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        EnsureProfileId(barberProfileId);
        ArgumentNullException.ThrowIfNull(request);

        var path = string.Concat(
            BuildProfilePath(barberProfileId, "time-offs"),
            ForceQuery(force));

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(request, options: ApiJson.Options),
        };

        using var response = await _httpClient
            .SendAsync(httpRequest, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveTimeOffAsync(
        Guid barberProfileId,
        Guid timeOffId,
        CancellationToken cancellationToken = default)
    {
        EnsureProfileId(barberProfileId);

        if (timeOffId == Guid.Empty)
        {
            throw new ArgumentException("The time off identifier must not be empty.", nameof(timeOffId));
        }

        var path = BuildProfilePath(
            barberProfileId,
            string.Concat("time-offs/", timeOffId.ToString("D", CultureInfo.InvariantCulture)));

        using var response = await _httpClient
            .DeleteAsync(path, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Собирает адрес чтения расписания.
    /// </summary>
    /// <remarks>
    /// Окно необязательно: без границ сервер отдаёт собственное окно исключений, и пустые
    /// <c>from=</c>/<c>to=</c> только засоряли бы адрес. Даты — календарные строки в
    /// инвариантной культуре, без пересчёта в UTC.
    /// </remarks>
    private static string BuildSchedulePath(Guid barberProfileId, DateOnly? from, DateOnly? toDate)
    {
        var path = BuildProfilePath(barberProfileId, "schedule");
        var parameters = new List<string>(2);

        if (from is { } fromDate)
        {
            parameters.Add(string.Concat(
                "from=",
                fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        if (toDate is { } toValue)
        {
            parameters.Add(string.Concat(
                "to=",
                toValue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        return parameters.Count == 0
            ? path
            : string.Concat(path, "?", string.Join('&', parameters));
    }

    /// <summary>
    /// <c>?force=true</c> только после подтверждения конфликта: обычный путь и его адрес
    /// не меняются.
    /// </summary>
    private static string ForceQuery(bool force) => force ? "?force=true" : string.Empty;

    /// <summary>
    /// Собирает адрес дочернего ресурса профиля.
    /// </summary>
    private static string BuildProfilePath(Guid barberProfileId, string tail) => string.Concat(
        "api/barber-profiles/",
        barberProfileId.ToString("D", CultureInfo.InvariantCulture),
        "/",
        tail);

    /// <summary>
    /// Собирает адрес ресурса пары (мастер, салон).
    /// </summary>
    private static string BuildSalonPath(Guid barberProfileId, Guid salonId, string tail) =>
        string.Concat(
            BuildProfilePath(barberProfileId, "salons/"),
            salonId.ToString("D", CultureInfo.InvariantCulture),
            "/",
            tail);

    /// <summary>
    /// Отсекает пустой идентификатор профиля до запроса.
    /// </summary>
    private static void EnsureProfileId(Guid barberProfileId)
    {
        if (barberProfileId == Guid.Empty)
        {
            throw new ArgumentException(
                "The barber profile identifier must not be empty.",
                nameof(barberProfileId));
        }
    }

    /// <summary>
    /// Отсекает пустой идентификатор салона до запроса.
    /// </summary>
    private static void EnsureSalonId(Guid salonId)
    {
        if (salonId == Guid.Empty)
        {
            throw new ArgumentException("The salon identifier must not be empty.", nameof(salonId));
        }
    }

    /// <summary>
    /// Бросает <see cref="BarberSearchApiException"/>, если ответ неуспешный.
    /// </summary>
    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}

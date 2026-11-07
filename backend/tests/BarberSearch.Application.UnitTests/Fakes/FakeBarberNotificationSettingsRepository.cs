using BarberSearch.Application.Notifications;
using BarberSearch.Domain.Notifications;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="IBarberNotificationSettingsRepository"/>: одна строка на пользователя,
/// как первичный ключ <c>user_id</c> в PostgreSQL.
/// </summary>
/// <remarks>
/// <see cref="GetEnabledAsync"/> по умолчанию возвращает все строки, а не только включённые:
/// так тест видит, что обработчик сам отбрасывает выключенного барбера, а не полагается
/// на фильтр хранилища.
/// </remarks>
public sealed class FakeBarberNotificationSettingsRepository
    : IBarberNotificationSettingsRepository
{
    private readonly Dictionary<Guid, BarberNotificationSettings> _settings = [];

    public IReadOnlyCollection<BarberNotificationSettings> All => _settings.Values;

    /// <summary>
    /// Возвращать ли из <see cref="GetEnabledAsync"/> только включённые строки.
    /// </summary>
    public bool FilterDisabledInGetEnabled { get; set; }

    public int AddCalls { get; private set; }

    public int UpdateCalls { get; private set; }

    public void Seed(BarberNotificationSettings settings)
        => _settings[settings.UserId] = settings;

    public Task<Result<BarberNotificationSettings?, Failure>> FindByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        _settings.TryGetValue(userId, out var settings);

        return Task.FromResult(
            Result.Success<BarberNotificationSettings?, Failure>(settings));
    }

    public Task<Result<IReadOnlyList<BarberNotificationSettings>, Failure>> GetEnabledAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<BarberNotificationSettings> rows = FilterDisabledInGetEnabled
            ? _settings.Values.Where(settings => settings.IsEnabled).ToList()
            : _settings.Values.ToList();

        return Task.FromResult(
            Result.Success<IReadOnlyList<BarberNotificationSettings>, Failure>(rows));
    }

    public Task<Result<BarberNotificationSettings?, Failure>> FindByTelegramLinkCodeHashAsync(
        string codeHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var settings = _settings.Values.FirstOrDefault(candidate =>
            candidate.TelegramLinkCodeHash == codeHash
            && candidate.TelegramLinkCodeExpiresAt > now);

        return Task.FromResult(
            Result.Success<BarberNotificationSettings?, Failure>(settings));
    }

    public Task<Result<bool, Failure>> IsChatLinkedToAnotherUserAsync(
        long chatId,
        Guid exceptUserId,
        CancellationToken cancellationToken = default)
    {
        var linked = _settings.Values.Any(candidate =>
            candidate.UserId != exceptUserId && candidate.TelegramChatId == chatId);

        return Task.FromResult(Result.Success<bool, Failure>(linked));
    }

    public Task<UnitResult<Failure>> AddAsync(
        BarberNotificationSettings settings,
        CancellationToken cancellationToken = default)
    {
        AddCalls++;

        if (_settings.ContainsKey(settings.UserId))
        {
            return Task.FromResult(UnitResult.Failure(new Failure(Error.Conflict(
                "notificationSettings.add.uniqueViolation",
                "Notification settings for this user already exist."))));
        }

        _settings[settings.UserId] = settings;

        return Task.FromResult(UnitResult.Success<Failure>());
    }

    public Task<UnitResult<Failure>> UpdateAsync(
        BarberNotificationSettings settings,
        CancellationToken cancellationToken = default)
    {
        UpdateCalls++;

        _settings[settings.UserId] = settings;

        return Task.FromResult(UnitResult.Success<Failure>());
    }
}

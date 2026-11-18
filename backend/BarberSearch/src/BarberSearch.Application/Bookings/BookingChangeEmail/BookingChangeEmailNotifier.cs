using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Emails;
using BarberSearch.Application.Push;
using BarberSearch.Application.Users;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.EmailOutbox;
using BarberSearch.Domain.Push;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Bookings.BookingChangeEmail;

/// <summary>
/// Постановка в очередь письма об отмене или переносе записи из мобильного приложения.
/// </summary>
/// <remarks>
/// <para>
/// Порядок тот же, что у push-уведомления: гейт поверхности и автора, чтение получателя,
/// снимок каталога, готовая строка outbox и <c>INSERT</c>. Запись в outbox — best-effort:
/// ошибка логируется предупреждением с идентификатором брони и кодами/типом ошибки, без
/// адреса и без текста письма (152-ФЗ), и не меняет результат команды.
/// </para>
/// <para>
/// Адрес без подтверждения не используется: опечатка при регистрации отправила бы детали
/// визита постороннему человеку. Нет пользователя, нет адреса, адрес не подтверждён — молча
/// пропускаем, без ошибки в ответе.
/// </para>
/// <para>
/// Повторная постановка того же письма — успех, а не ошибка: ключ дедупликации строится из
/// брони и повода, и конфликт уникального индекса означает, что строка уже есть
/// (AGENT.md §19).
/// </para>
/// </remarks>
public sealed class BookingChangeEmailNotifier : IBookingChangeEmailNotifier
{
    /// <summary>
    /// Код нарушения уникальности ключа дедупликации: повтор — успех (см. §19).
    /// </summary>
    private const string DUPLICATE_KEY_CODE = "emailOutbox.add.uniqueViolation";

    private readonly IClientSurface _clientSurface;
    private readonly IUserRepository _users;
    private readonly IEmailOutboxRepository _outbox;
    private readonly IPushSourceRepository _source;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BookingChangeEmailNotifier> _logger;

    public BookingChangeEmailNotifier(
        IClientSurface clientSurface,
        IUserRepository users,
        IEmailOutboxRepository outbox,
        IPushSourceRepository source,
        TimeProvider timeProvider,
        ILogger<BookingChangeEmailNotifier> logger)
    {
        ArgumentNullException.ThrowIfNull(clientSurface);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _clientSurface = clientSurface;
        _users = users;
        _outbox = outbox;
        _source = source;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task NotifyCancelledAsync(
        Booking booking,
        bool isClientOwner,
        CancellationToken cancellationToken = default)
        => NotifyAsync(
            booking,
            isClientOwner,
            PushNotificationKind.BookingCancelledByCustomer,
            cancellationToken);

    /// <inheritdoc />
    public Task NotifyRescheduledAsync(
        Booking booking,
        bool isClientOwner,
        CancellationToken cancellationToken = default)
        => NotifyAsync(
            booking,
            isClientOwner,
            PushNotificationKind.BookingRescheduledByCustomer,
            cancellationToken);

    private async Task NotifyAsync(
        Booking booking,
        bool isClientOwner,
        PushNotificationKind kind,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(booking);

        // Письмо — только замена push'у приложения: сотрудник и веб уведомляются иначе.
        if (!isClientOwner || !_clientSurface.IsMobileApp)
        {
            return;
        }

        try
        {
            await QueueAsync(booking, kind, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Сбой письма не должен менять результат уже сохранённой команды.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            // Тип, а не текст исключения: сообщение может содержать параметры SQL-запроса.
            _logger.LogWarning(
                "Booking {BookingId} change email failed: {ErrorType}.",
                booking.Id,
                exception.GetType().Name);
        }
    }

    private async Task QueueAsync(
        Booking booking,
        PushNotificationKind kind,
        CancellationToken cancellationToken)
    {
        // Гостевой брони письмо отправлять некуда: у неё нет аккаунта.
        if (booking.UserId is not { } userId)
        {
            return;
        }

        var userResult = await _users.GetByIdAsync(userId, cancellationToken);
        if (userResult.IsFailure)
        {
            // Нет пользователя — нет получателя; это не ошибка команды.
            return;
        }

        var user = userResult.Value;

        // Неподтверждённый адрес мог быть введён с опечаткой: чужие детали визита не уходят.
        if (!user.IsEmailVerified || string.IsNullOrWhiteSpace(user.Email.Value))
        {
            return;
        }

        var snapshotResult = await _source.GetBookingSnapshotAsync(
            booking.BarberProfileId,
            booking.BarberServiceId,
            booking.SalonId,
            cancellationToken);
        if (snapshotResult.IsFailure)
        {
            LogFailure(booking, "the booking snapshot could not be read", snapshotResult.Error);

            return;
        }

        // Пустой снимок деградирует текст так же, как у push: письмо важнее каталога.
        var snapshot = snapshotResult.Value ?? new PushBookingSnapshot(null, null, null, null);
        var now = _timeProvider.GetUtcNow();

        var message = BookingChangeMessage.Create(user.Email.Value, snapshot, booking.StartAt, kind, now);

        var row = EmailOutboxMessage.Create(
            DeduplicationKey(booking, kind),
            message.To,
            message.Subject,
            message.HtmlBody,
            message.TextBody,
            now);
        if (row.IsFailure)
        {
            LogFailure(booking, "the change email was rejected", row.Error);

            return;
        }

        var added = await _outbox.AddAsync(row.Value, cancellationToken);
        if (added.IsFailure && !IsDuplicateKey(added.Error))
        {
            LogFailure(booking, "the change email was not enqueued", added.Error);
        }
    }

    /// <summary>
    /// Ключ дедупликации письма: отмена — на бронь, перенос — на бронь и новое время.
    /// </summary>
    /// <remarks>
    /// Два переноса на разные моменты — два письма; повтор того же переноса (ретрай запроса,
    /// второй тик) — одна строка. У отмены времени в ключе нет: отменить запись дважды нельзя.
    /// </remarks>
    private static string DeduplicationKey(Booking booking, PushNotificationKind kind)
        => kind switch
        {
            PushNotificationKind.BookingCancelledByCustomer =>
                $"booking:{booking.Id:D}:cancelled",
            PushNotificationKind.BookingRescheduledByCustomer =>
                FormattableString.Invariant($"booking:{booking.Id:D}:rescheduled:{booking.StartAt:O}"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "The kind is not a customer booking change.")
        };

    private static bool IsDuplicateKey(Failure failure)
        => failure.Errors.Any(error => error.Code == DUPLICATE_KEY_CODE);

    /// <summary>
    /// Пишет предупреждение без адреса и текста письма: только бронь и коды ошибок.
    /// </summary>
    private void LogFailure(Booking booking, string what, Failure failure)
    {
        _logger.LogWarning(
            "Booking {BookingId} change email was skipped because {What}: {Codes}.",
            booking.Id,
            what,
            string.Join(", ", failure.Errors.Select(error => error.Code)));
    }
}

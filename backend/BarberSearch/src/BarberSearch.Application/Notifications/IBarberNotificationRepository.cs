using BarberSearch.Domain.Notifications;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Notifications;

/// <summary>
/// Доступ к сущности <see cref="BarberNotification"/>: инбокс, доставка и уборка.
/// </summary>
/// <remarks>
/// <para>
/// Дедупликация — обязанность хранилища, а не обработчика: повторный тик воркера и второй
/// экземпляр приложения обязаны быть безопасны, поэтому вставка идёт с
/// <c>ON CONFLICT DO NOTHING</c> по уникальным индексам <c>ux_barber_notifications_upcoming</c>
/// (<c>booking_id where kind = 1</c>) и <c>ux_barber_notifications_digest</c>
/// (<c>user_id, digest_local_date where kind = 2</c>). <see cref="TryAddAsync"/> сообщает,
/// была ли строка вставлена на самом деле, — по этому признаку обработчик считает созданные.
/// </para>
/// <para>
/// Методы, которые меняют состояние, сохраняют изменения сразу: у уведомления нет второго
/// участника изменения, поэтому транзакция на несколько агрегатов не нужна.
/// </para>
/// </remarks>
public interface IBarberNotificationRepository
{
    /// <summary>
    /// Страница инбокса барбера, свежие сверху (<c>created_at DESC, id DESC</c>).
    /// </summary>
    /// <remarks>
    /// Keyset-условие берётся из <paramref name="cursor"/>; <paramref name="limit"/> — это
    /// pageSize + 1, чтобы обработчик отличил последнюю страницу от неполной. Читается
    /// без отслеживания.
    /// </remarks>
    /// <param name="userId">Барбер-получатель.</param>
    /// <param name="cursor">Позиция продолжения или <c>null</c> для первой страницы.</param>
    /// <param name="limit">Сколько строк прочитать (pageSize + 1).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<IReadOnlyList<BarberNotification>, Failure>> GetPageAsync(
        Guid userId,
        BarberNotificationCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Число непрочитанных уведомлений барбера (<c>read_at is null</c>).
    /// </summary>
    /// <param name="userId">Барбер-получатель.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<int, Failure>> CountUnreadAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Уведомление по идентификатору. Сущность отслеживается контекстом.
    /// </summary>
    /// <remarks>
    /// Отсутствующая строка — <c>Error.NotFound</c> с кодом <c>notification.notFound</c>.
    /// Владение проверяет обработчик: чужое уведомление для него тоже «не найдено».
    /// </remarks>
    /// <param name="id">Идентификатор уведомления.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<BarberNotification, Failure>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Пытается вставить уведомление, не нарушая дедупликацию.
    /// </summary>
    /// <remarks>
    /// Повтор по уникальному индексу (тот же визит для <c>Upcoming</c>, та же локальная дата
    /// для <c>Digest</c>) не ошибка: метод возвращает <c>false</c>, и обработчик не считает
    /// строку созданной. Ошибка БД любого другого рода — <c>Failure</c>.
    /// </remarks>
    /// <param name="notification">Новое уведомление в состоянии <c>Pending</c>.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><c>true</c>, если строка вставлена; <c>false</c>, если такая уже есть.</returns>
    Task<Result<bool, Failure>> TryAddAsync(
        BarberNotification notification,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохраняет изменения уведомления (доставка, прочтение).
    /// </summary>
    /// <param name="notification">Отслеживаемая сущность.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<UnitResult<Failure>> UpdateAsync(
        BarberNotification notification,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Помечает прочитанными все непрочитанные уведомления барбера одним запросом.
    /// </summary>
    /// <remarks>
    /// Идемпотентно: повторный вызов возвращает <c>0</c> и ничего не меняет. <c>read_at</c>
    /// уже прочитанных строк не перезаписывается.
    /// </remarks>
    /// <param name="userId">Барбер-получатель.</param>
    /// <param name="readAt">Момент прочтения в UTC — часы Application.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сколько строк изменено.</returns>
    Task<Result<int, Failure>> MarkAllReadAsync(
        Guid userId,
        DateTimeOffset readAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Пачка уведомлений к доставке: <c>Pending</c>, меньше пяти попыток, время попытки наступило.
    /// </summary>
    /// <remarks>
    /// Порядок — <c>created_at</c>, чтобы старое не голодало. Условие выборки:
    /// <c>delivery = 0 and delivery_attempts &lt; 5 and (next_attempt_at is null or next_attempt_at &lt;= now)</c>.
    /// Два экземпляра приложения не должны отправлять одно сообщение дважды, поэтому выборка
    /// обязана атомарно «забирать» строки (например, <c>FOR UPDATE SKIP LOCKED</c> в транзакции
    /// шага или условный <c>UPDATE ... RETURNING</c>); конкретный механизм — забота реализации
    /// хранилища, а не обработчика. Обработчик лишь считает, что пачка принадлежит ему.
    /// </remarks>
    /// <param name="now">Текущий момент UTC — граница <c>next_attempt_at</c>.</param>
    /// <param name="batchSize">Размер пачки (по умолчанию 100).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<IReadOnlyList<BarberNotification>, Failure>> GetPendingForDeliveryAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Удаляет уведомления старше момента отсечения (ретенция 180 дней).
    /// </summary>
    /// <param name="cutoff">Граница ретенции: всё, что создано раньше, удаляется.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сколько строк удалено.</returns>
    Task<Result<int, Failure>> DeleteOlderThanAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default);
}

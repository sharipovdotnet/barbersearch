using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.Common;
using CSharpFunctionalExtensions;
using Shared;
using Entity = BarberSearch.Domain.Common.Entity;

namespace BarberSearch.Domain.PortfolioPhotos;

/// <summary>
/// Фотография портфолио. Дочерняя сущность агрегата <see cref="BarberProfile"/>:
/// создаётся только методами агрегата, собственного публичного конструктора нет.
/// </summary>
public class PortfolioPhoto : Entity
{
    public Guid BarberProfileId { get; private set; }

    public string Url { get; private set; } = null!;

    public string? ThumbnailUrl { get; private set; }

    public string? Description { get; private set; }

    public int SortOrder { get; private set; }

    /// <summary>
    /// Намерение владельца: опубликована ли фотография. Снятие с публикации — обратимое
    /// действие владельца, отдельное от административной блокировки и от допуска к публикации.
    /// </summary>
    /// <remarks>
    /// Само по себе значение ничего не публикует: публичная карточка требует ещё
    /// <see cref="ModerationStatus"/> = <see cref="PhotoModerationStatus.Approved"/>
    /// и <see cref="IsBlocked"/> = <c>false</c>. Снятие с публикации статус модерации
    /// не меняет и кадр в очередь не возвращает.
    /// </remarks>
    public bool IsPublished { get; private set; }

    /// <summary>
    /// Административная маска: фотография скрыта администратором.
    /// </summary>
    /// <remarks>
    /// Отдельный флаг, а не значение <see cref="IsPublished"/>: снять блокировку владелец
    /// не может, это делает только администратор. Заблокированная фотография не попадает
    /// в публичную карточку и не раздаётся по URL, но её статус модерации не меняется.
    /// </remarks>
    public bool IsBlocked { get; private set; }

    /// <summary>
    /// Допуск кадра к публикации: решение администратора о самом кадре.
    /// </summary>
    /// <remarks>
    /// Три независимых признака: <see cref="ModerationStatus"/> — допуск к публикации,
    /// <see cref="IsPublished"/> — намерение владельца, <see cref="IsBlocked"/> —
    /// административная маска. Публичная карточка показывает фотографию, только когда
    /// выполнены все три условия. Новый кадр создаётся
    /// <see cref="PhotoModerationStatus.Pending"/> и ждёт решения.
    /// </remarks>
    public PhotoModerationStatus ModerationStatus { get; private set; }

    /// <summary>
    /// Причина отказа из <see cref="Reject"/>; <c>null</c> — кадр не отклонён.
    /// </summary>
    public string? RejectionReason { get; private set; }

    /// <summary>
    /// Когда администратор принял решение; <c>null</c> — кадр ещё ждёт проверки.
    /// </summary>
    public DateTimeOffset? ReviewedAt { get; private set; }

    /// <summary>
    /// Кто принял решение; <c>null</c> — кадр ещё ждёт проверки.
    /// </summary>
    public Guid? ReviewedByUserId { get; private set; }

    // Навигация внутри агрегата (ребёнок -> родитель): нужна EF Core для связи.
    public BarberProfile BarberProfile { get; private set; } = null!;

    private const string REJECTION_REASON_REQUIRED_CODE = "portfolioPhoto.moderation.reasonRequired";
    private const string REJECTION_REASON_TOO_LONG_CODE = "portfolioPhoto.moderation.reasonTooLong";
    private const string INVALID_STATUS_CODE = "portfolioPhoto.moderation.invalidStatus";

    private PortfolioPhoto()
    {
    }

    private PortfolioPhoto(
        Guid barberProfileId,
        string url,
        string? thumbnailUrl,
        string? description,
        int sortOrder)
    {
        BarberProfileId = barberProfileId;
        Url = url;
        ThumbnailUrl = thumbnailUrl;
        Description = description;
        SortOrder = sortOrder;

        IsPublished = true;
        IsBlocked = false;

        // Премодерация: ни один новый кадр не публикуется до решения администратора.
        ModerationStatus = PhotoModerationStatus.Pending;
    }

    /// <summary>
    /// Создаёт фотографию от имени агрегата. BarberProfileId и порядок проставляются только
    /// агрегатом, поэтому «чужого» ребёнка создать нельзя.
    /// </summary>
    /// <remarks>
    /// <see cref="IsPublished"/> = <c>true</c> — это намерение владельца опубликовать кадр,
    /// а не разрешение: фотография создаётся <see cref="PhotoModerationStatus.Pending"/>
    /// и появится публично только после <see cref="Approve"/>.
    /// </remarks>
    internal static PortfolioPhoto Create(
        Guid barberProfileId,
        string url,
        string? thumbnailUrl,
        string? description,
        int sortOrder)
        => new(barberProfileId, url, thumbnailUrl, description, sortOrder);

    /// <summary>
    /// Одобряет кадр: он допускается к публикации по обычным правилам
    /// (<see cref="IsPublished"/> и <see cref="IsBlocked"/>).
    /// </summary>
    /// <remarks>
    /// Одобрить можно только кадр в статусе <see cref="PhotoModerationStatus.Pending"/>:
    /// повторное решение — <c>409 portfolioPhoto.moderation.invalidStatus</c>. Причина
    /// отклонения очищается: одобренный кадр её больше не имеет.
    /// </remarks>
    /// <param name="adminId">Администратор, принявший решение.</param>
    /// <param name="now">Момент решения в UTC.</param>
    public UnitResult<Failure> Approve(Guid adminId, DateTimeOffset now)
    {
        if (ModerationStatus != PhotoModerationStatus.Pending)
        {
            return ReviewInvalidStatus();
        }

        ModerationStatus = PhotoModerationStatus.Approved;
        RejectionReason = null;
        ReviewedByUserId = adminId;
        ReviewedAt = now;

        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Отклоняет кадр с обязательной причиной: публично он не показывается.
    /// </summary>
    /// <remarks>
    /// Причина тримится и хранится без окружающих пробелов; пустая причина —
    /// <c>400 portfolioPhoto.moderation.reasonRequired</c>, длиннее
    /// <see cref="Constants.MAX_PHOTO_REJECTION_REASON_LENGTH"/> символов —
    /// <c>400 portfolioPhoto.moderation.reasonTooLong</c>. Отклонить можно только
    /// <see cref="PhotoModerationStatus.Pending"/>-кадр, повторное решение —
    /// <c>409 portfolioPhoto.moderation.invalidStatus</c>.
    /// </remarks>
    /// <param name="adminId">Администратор, принявший решение.</param>
    /// <param name="reason">Причина отказа.</param>
    /// <param name="now">Момент решения в UTC.</param>
    public UnitResult<Failure> Reject(Guid adminId, string? reason, DateTimeOffset now)
    {
        if (ModerationStatus != PhotoModerationStatus.Pending)
        {
            return ReviewInvalidStatus();
        }

        var normalizedReason = reason?.Trim();

        if (string.IsNullOrEmpty(normalizedReason))
        {
            return new Failure(Error.Validation(
                REJECTION_REASON_REQUIRED_CODE,
                "A rejection reason is required.",
                nameof(reason)));
        }

        if (normalizedReason.Length > Constants.MAX_PHOTO_REJECTION_REASON_LENGTH)
        {
            return new Failure(Error.Validation(
                REJECTION_REASON_TOO_LONG_CODE,
                $"""
                 A rejection reason cannot be longer than
                 {Constants.MAX_PHOTO_REJECTION_REASON_LENGTH} characters.
                 """,
                nameof(reason)));
        }

        ModerationStatus = PhotoModerationStatus.Rejected;
        RejectionReason = normalizedReason;
        ReviewedByUserId = adminId;
        ReviewedAt = now;

        MarkUpdated();

        return DomainErrors.Ok();
    }

    public UnitResult<Failure> UpdateDescription(string? description)
    {
        if (description is not null && description.Length > Constants.MAX_TEXT_LENGTH)
        {
            return new Failure(Error.Validation(
                "portfolioPhoto.updateDescription.description",
                DomainMessages.PHOTO_DESCRIPTION_TOO_LONG,
                nameof(description)));
        }

        Description = description;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Меняет порядок вывода фотографии в портфолио.
    /// </summary>
    /// <remarks>
    /// Публичная точка входа одна — <see cref="BarberProfile.ReorderPhoto"/> и
    /// <see cref="BarberProfile.ReorderPhotos"/>, которые проверяют, что фотография
    /// принадлежит этому профилю, и обновляют корень агрегата. Метод <c>internal</c> (R-44):
    /// ребёнок не меняется снаружи, и дублирующего маршрута <c>…/photos/{photoId}/order</c>
    /// больше нет.
    /// </remarks>
    internal UnitResult<Failure> Reorder(int sortOrder)
    {
        SortOrder = sortOrder;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    public UnitResult<Failure> Publish()
    {
        IsPublished = true;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    public UnitResult<Failure> Unpublish()
    {
        IsPublished = false;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Блокирует фотографию: она пропадает из публичной карточки и перестаёт раздаваться.
    /// Идемпотентно; вызывается только администратором.
    /// </summary>
    public UnitResult<Failure> Block()
    {
        IsBlocked = true;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Снимает административную блокировку. Идемпотентно; владелец профиля вызвать не может.
    /// </summary>
    public UnitResult<Failure> Unblock()
    {
        IsBlocked = false;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    private static Failure ReviewInvalidStatus()
        => new(Error.Conflict(
            INVALID_STATUS_CODE,
            "Only a photo pending moderation can be reviewed."));
}

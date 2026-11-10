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
    /// Опубликована ли фотография владельцем. Снятие с публикации — обратимое действие
    /// владельца, отдельное от административной блокировки.
    /// </summary>
    public bool IsPublished { get; private set; }

    /// <summary>
    /// Заблокирована ли фотография администратором.
    /// </summary>
    /// <remarks>
    /// Отдельный флаг, а не значение <see cref="IsPublished"/>: снять блокировку владелец
    /// не может, это делает только администратор. Заблокированная фотография не попадает
    /// в публичную карточку и не раздаётся по URL.
    /// </remarks>
    public bool IsBlocked { get; private set; }

    // Навигация внутри агрегата (ребёнок -> родитель): нужна EF Core для связи.
    public BarberProfile BarberProfile { get; private set; } = null!;

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
    }

    /// <summary>
    /// Создаёт фотографию от имени агрегата. BarberProfileId и порядок проставляются только
    /// агрегатом, поэтому «чужого» ребёнка создать нельзя.
    /// </summary>
    internal static PortfolioPhoto Create(
        Guid barberProfileId,
        string url,
        string? thumbnailUrl,
        string? description,
        int sortOrder)
        => new(barberProfileId, url, thumbnailUrl, description, sortOrder);

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
}

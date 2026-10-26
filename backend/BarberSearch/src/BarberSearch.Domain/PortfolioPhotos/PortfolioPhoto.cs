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

    public bool IsPublished { get; private set; }

    // Навигация внутри агрегата (ребёнок -> родитель): нужна EF Core для связи.
    public BarberProfile BarberProfile { get; private set; } = null!;

    private PortfolioPhoto()
    {
    }

    private PortfolioPhoto(
        Guid barberProfileId,
        string url,
        string? thumbnailUrl,
        string? description)
    {
        BarberProfileId = barberProfileId;
        Url = url;
        ThumbnailUrl = thumbnailUrl;
        Description = description;

        IsPublished = true;
    }

    /// <summary>
    /// Создаёт фотографию от имени агрегата. BarberProfileId проставляет только агрегат,
    /// поэтому «чужого» ребёнка создать нельзя.
    /// </summary>
    internal static PortfolioPhoto Create(
        Guid barberProfileId,
        string url,
        string? thumbnailUrl,
        string? description)
        => new(barberProfileId, url, thumbnailUrl, description);

    public UnitResult<Failure> UpdateDescription(string? description)
    {
        if (description is not null && description.Length > Constants.MAX_TEXT_LENGTH)
        {
            return new Failure(Error.Validation(
                "portfolioPhoto.updateDescription.description",
                $"Photo description cannot be longer than {Constants.MAX_TEXT_LENGTH} characters.",
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
    /// Публичная точка входа одна — <see cref="BarberProfile.ReorderPhoto"/>, которая проверяет,
    /// что фотография принадлежит этому профилю, и обновляет корень агрегата. Метод
    /// <c>internal</c> (R-44): ребёнок не меняется снаружи, и дублирующего маршрута
    /// <c>…/photos/{photoId}/order</c> больше нет.
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
}

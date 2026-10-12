using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.Common;

namespace BarberSearch.Domain.PortfolioPhotos;

public class PortfolioPhoto : Entity
{
    public Guid BarberProfileId { get; private set; }

    public string Url { get; private set; } = null!;

    public string? ThumbnailUrl { get; private set; }

    public string? Description { get; private set; }

    public int SortOrder { get; private set; }

    public bool IsPublished { get; private set; }

    public BarberProfile BarberProfile { get; private set; } = null!;

    private PortfolioPhoto()
    {
    }

    public PortfolioPhoto(
        Guid barberProfileId,
        string url,
        string? thumbnailUrl = null,
        string? description = null)
    {
        BarberProfileId = barberProfileId;
        Url = url;
        ThumbnailUrl = thumbnailUrl;
        Description = description;

        IsPublished = true;
    }

    public void Update(
        string? description,
        int sortOrder)
    {
        Description = description;
        SortOrder = sortOrder;

        MarkUpdated();
    }

    public void Publish()
    {
        IsPublished = true;
        MarkUpdated();
    }

    public void Unpublish()
    {
        IsPublished = false;
        MarkUpdated();
    }
}
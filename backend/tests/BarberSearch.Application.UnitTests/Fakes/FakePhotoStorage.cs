using BarberSearch.Application.PortfolioPhotos;
using BarberSearch.Domain.PortfolioPhotos;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="IPhotoStorage"/>: запоминает сохранённые и удалённые URL и позволяет
/// подменить результат сохранения или падение удаления.
/// </summary>
/// <remarks>
/// URL собирается тем же <see cref="PortfolioMediaUrl"/>, что и в настоящем хранилище:
/// обработчик получает ссылку, которую домен обязан принять.
/// </remarks>
public sealed class FakePhotoStorage : IPhotoStorage
{
    public List<string> SavedUrls { get; } = [];

    public List<Guid> SavedProfileIds { get; } = [];

    public List<Guid> SavedMediaIds { get; } = [];

    public List<string> SavedExtensions { get; } = [];

    public List<byte[]> SavedContents { get; } = [];

    public List<string> DeletedUrls { get; } = [];

    public int SaveCallCount => SavedUrls.Count;

    /// <summary>Подмена ошибки сохранения: строка в БД появляться не должна.</summary>
    public Failure? SaveFailure { get; set; }

    /// <summary>Исключение при удалении: обработчик обязан проглотить его, а не упасть.</summary>
    public Exception? DeleteException { get; set; }

    public Task<Result<StoredPhoto, Failure>> SaveAsync(
        Guid barberProfileId,
        Guid mediaId,
        string extension,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        if (SaveFailure is { } failure)
        {
            return Task.FromResult<Result<StoredPhoto, Failure>>(failure);
        }

        var url = PortfolioMediaUrl.Build(barberProfileId, mediaId, extension);

        SavedUrls.Add(url);
        SavedProfileIds.Add(barberProfileId);
        SavedMediaIds.Add(mediaId);
        SavedExtensions.Add(extension);
        SavedContents.Add(content.ToArray());

        return Task.FromResult<Result<StoredPhoto, Failure>>(new StoredPhoto(url));
    }

    public Task<Result<PhotoContent, Failure>> OpenReadAsync(
        string url,
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task DeleteAsync(string url, CancellationToken cancellationToken)
    {
        DeletedUrls.Add(url);

        return DeleteException is null
            ? Task.CompletedTask
            : Task.FromException(DeleteException);
    }
}

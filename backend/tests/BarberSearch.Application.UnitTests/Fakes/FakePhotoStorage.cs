using BarberSearch.Application.PortfolioPhotos;
using BarberSearch.Domain.PortfolioPhotos;
using BarberSearch.Domain.Users;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="IPhotoStorage"/>: запоминает сохранённые и удалённые URL и позволяет
/// подменить результат сохранения или падение удаления. Обслуживает оба вида файлов —
/// фотографии портфолио и аватары, как настоящий адаптер.
/// </summary>
/// <remarks>
/// URL собирается теми же <see cref="PortfolioMediaUrl"/> и <see cref="UserAvatarMediaUrl"/>,
/// что и в настоящем хранилище: обработчик получает ссылку, которую домен обязан принять.
/// </remarks>
public sealed class FakePhotoStorage : IPhotoStorage
{
    public List<string> SavedUrls { get; } = [];

    public List<Guid> SavedProfileIds { get; } = [];

    public List<Guid> SavedMediaIds { get; } = [];

    public List<string> SavedExtensions { get; } = [];

    public List<byte[]> SavedContents { get; } = [];

    public List<string> SavedAvatarUrls { get; } = [];

    public List<Guid> SavedAvatarUserIds { get; } = [];

    public List<Guid> SavedAvatarMediaIds { get; } = [];

    public List<string> SavedAvatarExtensions { get; } = [];

    public List<byte[]> SavedAvatarContents { get; } = [];

    public List<string> DeletedUrls { get; } = [];

    public int SaveCallCount => SavedUrls.Count;

    public int AvatarSaveCallCount => SavedAvatarUrls.Count;

    /// <summary>Подмена ошибки сохранения: строка в БД появляться не должна.</summary>
    public Failure? SaveFailure { get; set; }

    /// <summary>
    /// Подмена URL, который «вернуло» хранилище аватара. Нужна, чтобы проверить ветку
    /// отказа <c>User.SetAvatar</c>: настоящий адаптер всегда собирает корректный media-URL.
    /// </summary>
    public string? AvatarUrlOverride { get; set; }

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

    public Task<Result<StoredPhoto, Failure>> SaveAvatarAsync(
        Guid userId,
        Guid mediaId,
        string extension,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        if (SaveFailure is { } failure)
        {
            return Task.FromResult<Result<StoredPhoto, Failure>>(failure);
        }

        var url = AvatarUrlOverride ?? UserAvatarMediaUrl.Build(userId, mediaId, extension);

        SavedAvatarUrls.Add(url);
        SavedAvatarUserIds.Add(userId);
        SavedAvatarMediaIds.Add(mediaId);
        SavedAvatarExtensions.Add(extension);
        SavedAvatarContents.Add(content.ToArray());

        return Task.FromResult<Result<StoredPhoto, Failure>>(new StoredPhoto(url));
    }

    public Task<Result<PhotoContent, Failure>> OpenReadAsync(
        string url,
        PhotoVariant variant,
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

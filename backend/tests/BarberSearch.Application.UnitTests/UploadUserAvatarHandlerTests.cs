using BarberSearch.Application.Users.UploadUserAvatar;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.Common;
using BarberSearch.Domain.Users;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Загрузка аватара: права до файла, общий инспектор, компенсация нового файла, удаление
/// предыдущего только после успешной записи в БД и логи без URL.
/// </summary>
public class UploadUserAvatarHandlerTests
{
    [Fact]
    public async Task Upload_ByCurrentUser_SavesFileAndAvatarAndReturnsUrl()
    {
        var user = CreateUser();
        var repository = new FakeUserRepository();
        repository.Add(user);
        var storage = new FakePhotoStorage();
        var content = TestImages.Png(800, 600);
        var logger = new CapturingLogger<UploadUserAvatarHandler>();

        var result = await CreateHandler(repository, storage, user.Id, logger).Handle(
            new UploadUserAvatarCommand(content),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(user.Id, Assert.Single(storage.SavedAvatarUserIds));
        Assert.Equal("png", Assert.Single(storage.SavedAvatarExtensions));
        Assert.Equal(content, Assert.Single(storage.SavedAvatarContents));
        Assert.Equal(Assert.Single(storage.SavedAvatarUrls), result.Value.AvatarUrl);
        Assert.Equal(result.Value.AvatarUrl, user.AvatarUrl);

        // Премодерация: загрузка создаёт Pending-кадр, и ответ это подтверждает.
        Assert.Equal(PhotoModerationStatus.Pending, result.Value.ModerationStatus);
        Assert.Equal(PhotoModerationStatus.Pending, user.AvatarModerationStatus);
        Assert.NotNull(user.AvatarSubmittedAt);

        Assert.Equal(1, repository.UpdateCallCount);
        Assert.Empty(storage.DeletedUrls);
    }

    [Fact]
    public async Task Upload_WhenAnonymous_ReturnsUnauthorizedBeforeReadingTheFile()
    {
        var user = CreateUser();
        var repository = new FakeUserRepository();
        repository.Add(user);
        var storage = new FakePhotoStorage();

        var result = await new UploadUserAvatarHandler(
                repository,
                storage,
                FakeCurrentUser.Anonymous(),
                new CapturingLogger<UploadUserAvatarHandler>())
            .Handle(
                new UploadUserAvatarCommand(TestImages.SmallerThanMinimalFile()),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.UNAUTHORIZED, Assert.Single(result.Error.Errors).Type);
        Assert.Equal("auth.unauthorized", result.Error.Errors[0].Code);

        // Права проверяются до пользователя и до файла: ни чтения, ни записи.
        Assert.Equal(0, repository.GetByIdCallCount);
        Assert.Equal(0, storage.AvatarSaveCallCount);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("unsupported")]
    public async Task Upload_WithInvalidFile_ReturnsInspectorCodeWithoutStorage(string kind)
    {
        var user = CreateUser();
        var repository = new FakeUserRepository();
        repository.Add(user);
        var storage = new FakePhotoStorage();
        var content = kind == "empty"
            ? TestImages.SmallerThanMinimalFile()
            : TestImages.Gif();

        var result = await CreateHandler(repository, storage, user.Id).Handle(
            new UploadUserAvatarCommand(content),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, Assert.Single(result.Error.Errors).Type);
        Assert.Equal(
            kind == "empty"
                ? "barberProfile.uploadPhoto.empty"
                : "barberProfile.uploadPhoto.unsupportedType",
            result.Error.Errors[0].Code);
        Assert.Equal(0, storage.AvatarSaveCallCount);
        Assert.Equal(0, repository.UpdateCallCount);
        Assert.Null(user.AvatarUrl);
    }

    [Fact]
    public async Task Upload_WithTooLargeFile_ReturnsTooLargeWithoutStorage()
    {
        var user = CreateUser();
        var repository = new FakeUserRepository();
        repository.Add(user);
        var storage = new FakePhotoStorage();

        var result = await CreateHandler(repository, storage, user.Id).Handle(
            new UploadUserAvatarCommand(TestImages.LargerThanLimit()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            "barberProfile.uploadPhoto.tooLarge",
            Assert.Single(result.Error.Errors).Code);
        Assert.Equal(0, storage.AvatarSaveCallCount);
    }

    [Fact]
    public async Task Upload_WhenStorageFails_DoesNotTouchTheUser()
    {
        var user = CreateUser();
        var repository = new FakeUserRepository();
        repository.Add(user);
        var storage = new FakePhotoStorage
        {
            SaveFailure = new Failure(Error.Failure(
                "user.uploadAvatar.storage",
                "The avatar file could not be stored."))
        };

        var result = await CreateHandler(repository, storage, user.Id).Handle(
            new UploadUserAvatarCommand(TestImages.Png(800, 600)),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("user.uploadAvatar.storage", Assert.Single(result.Error.Errors).Code);
        Assert.Null(user.AvatarUrl);
        Assert.Equal(0, repository.UpdateCallCount);
        Assert.Empty(storage.DeletedUrls);
    }

    [Fact]
    public async Task Upload_WhenSetAvatarFails_DeletesNewFileAndKeepsPreviousAvatar()
    {
        var user = CreateUser();
        var previousUrl = UserAvatarMediaUrl.Build(user.Id, Guid.CreateVersion7(), "png");
        Assert.True(user.SetAvatar(previousUrl).IsSuccess);

        var repository = new FakeUserRepository();
        repository.Add(user);
        var storage = new FakePhotoStorage
        {
            // Чужой media-URL: домен обязан отклонить его в SetAvatar.
            AvatarUrlOverride = UserAvatarMediaUrl.Build(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                "png")
        };

        var result = await CreateHandler(repository, storage, user.Id).Handle(
            new UploadUserAvatarCommand(TestImages.Png(800, 600)),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("user.setAvatar.avatarUrl", Assert.Single(result.Error.Errors).Code);
        Assert.Equal(previousUrl, user.AvatarUrl);

        // Компенсация удаляет именно новый файл; предыдущий не тронут.
        Assert.Equal(storage.AvatarUrlOverride, Assert.Single(storage.DeletedUrls));
        Assert.DoesNotContain(previousUrl, storage.DeletedUrls);
        Assert.Equal(0, repository.UpdateCallCount);
    }

    [Fact]
    public async Task Upload_WhenRepositoryFails_DeletesNewFileAndKeepsPreviousAvatar()
    {
        var user = CreateUser();
        var previousUrl = UserAvatarMediaUrl.Build(user.Id, Guid.CreateVersion7(), "jpg");
        Assert.True(user.SetAvatar(previousUrl).IsSuccess);

        var repository = new FakeUserRepository();
        repository.Add(user);
        repository.UpdateBehavior = _ => new Failure(Error.Failure(
            "internal.database",
            "The database rejected the change."));
        var storage = new FakePhotoStorage();

        var result = await CreateHandler(repository, storage, user.Id).Handle(
            new UploadUserAvatarCommand(TestImages.Jpeg(800, 600)),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("internal.database", Assert.Single(result.Error.Errors).Code);

        var newUrl = Assert.Single(storage.SavedAvatarUrls);

        Assert.Equal(newUrl, Assert.Single(storage.DeletedUrls));
        Assert.DoesNotContain(previousUrl, storage.DeletedUrls);
        Assert.Equal(1, repository.UpdateCallCount);
    }

    [Fact]
    public async Task Upload_AfterSuccess_DeletesPreviousOwnAvatarFile()
    {
        var user = CreateUser();
        var previousUrl = UserAvatarMediaUrl.Build(user.Id, Guid.CreateVersion7(), "webp");
        Assert.True(user.SetAvatar(previousUrl).IsSuccess);

        var repository = new FakeUserRepository();
        repository.Add(user);
        var storage = new FakePhotoStorage();

        var result = await CreateHandler(repository, storage, user.Id).Handle(
            new UploadUserAvatarCommand(TestImages.Png(800, 600)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var newUrl = Assert.Single(storage.SavedAvatarUrls);

        Assert.Equal(newUrl, result.Value.AvatarUrl);
        Assert.Equal(previousUrl, Assert.Single(storage.DeletedUrls));
        Assert.NotEqual(newUrl, storage.DeletedUrls[0]);
    }

    [Fact]
    public async Task Upload_WithExternalPreviousAvatar_DoesNotTouchStorageForIt()
    {
        const string externalUrl = "https://cdn.example.com/legacy-avatar.png";

        var user = CreateUser();
        Assert.True(user.SetAvatar(externalUrl).IsSuccess);

        var repository = new FakeUserRepository();
        repository.Add(user);
        var storage = new FakePhotoStorage();

        var result = await CreateHandler(repository, storage, user.Id).Handle(
            new UploadUserAvatarCommand(TestImages.Png(800, 600)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        // Внешняя ссылка не файл этого хранилища: удалять нечего, вызова нет.
        Assert.Empty(storage.DeletedUrls);
    }

    [Fact]
    public async Task Upload_LogsOnlyUserIdSizeAndDimensions()
    {
        var user = CreateUser();
        var repository = new FakeUserRepository();
        repository.Add(user);
        var storage = new FakePhotoStorage();
        var logger = new CapturingLogger<UploadUserAvatarHandler>();

        var result = await CreateHandler(repository, storage, user.Id, logger).Handle(
            new UploadUserAvatarCommand(TestImages.Png(800, 600)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var entry = Assert.Single(logger.Entries);

        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains(user.Id.ToString(), entry.Message, StringComparison.Ordinal);
        Assert.Contains("2048 bytes", entry.Message, StringComparison.Ordinal);
        Assert.Contains("800x600", entry.Message, StringComparison.Ordinal);

        // Ни URL, ни имени файла, ни пути в журнале нет.
        Assert.DoesNotContain(result.Value.AvatarUrl, entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(".png", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("media/avatars", entry.Message, StringComparison.Ordinal);
    }

    private static UploadUserAvatarHandler CreateHandler(
        FakeUserRepository repository,
        FakePhotoStorage storage,
        Guid userId,
        CapturingLogger<UploadUserAvatarHandler>? logger = null)
        => new(
            repository,
            storage,
            new FakeCurrentUser(userId),
            logger ?? new CapturingLogger<UploadUserAvatarHandler>());

    private static User CreateUser()
        => User.Create(
            $"avatar-{Guid.NewGuid():N}@barbersearch.test",
            "hash",
            "Ivan",
            "Petrov").Value;

    /// <summary>
    /// Журнал в памяти: тест проверяет уровень и текст записей обработчика.
    /// </summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
    }

    private sealed record LogEntry(LogLevel Level, string Message);
}

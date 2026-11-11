using BarberSearch.Application.Users.DeleteUserAvatar;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.Users;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Удаление аватара: идемпотентность, порядок «строка → файл» и отсутствие удаления файла
/// при неудачной записи в БД.
/// </summary>
public class DeleteUserAvatarHandlerTests
{
    [Fact]
    public async Task Delete_WithoutAvatar_SucceedsWithoutUpdateAndStorage()
    {
        var user = CreateUser();
        var repository = new FakeUserRepository();
        repository.Add(user);
        var storage = new FakePhotoStorage();

        var result = await CreateHandler(repository, storage, user.Id).Handle(
            new DeleteUserAvatarCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, repository.UpdateCallCount);
        Assert.Empty(storage.DeletedUrls);
        Assert.Null(user.AvatarUrl);
    }

    [Fact]
    public async Task Delete_WithOwnAvatar_ClearsItAndDeletesTheFile()
    {
        var user = CreateUser();
        var avatarUrl = UserAvatarMediaUrl.Build(user.Id, Guid.CreateVersion7(), "jpg");
        Assert.True(user.SetAvatar(avatarUrl).IsSuccess);

        var repository = new FakeUserRepository();
        repository.Add(user);
        var storage = new FakePhotoStorage();

        var result = await CreateHandler(repository, storage, user.Id).Handle(
            new DeleteUserAvatarCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(user.AvatarUrl);
        Assert.Equal(1, repository.UpdateCallCount);
        Assert.Equal(avatarUrl, Assert.Single(storage.DeletedUrls));
    }

    [Fact]
    public async Task Delete_WhenRepositoryFails_DoesNotDeleteTheFile()
    {
        var user = CreateUser();
        var avatarUrl = UserAvatarMediaUrl.Build(user.Id, Guid.CreateVersion7(), "png");
        Assert.True(user.SetAvatar(avatarUrl).IsSuccess);

        var repository = new FakeUserRepository();
        repository.Add(user);
        repository.UpdateBehavior = _ => new Failure(Error.Failure(
            "internal.database",
            "The database rejected the change."));
        var storage = new FakePhotoStorage();

        var result = await CreateHandler(repository, storage, user.Id).Handle(
            new DeleteUserAvatarCommand(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("internal.database", Assert.Single(result.Error.Errors).Code);

        // Строка не сохранилась — файл остаётся действующим аватаром.
        Assert.Empty(storage.DeletedUrls);
        Assert.Equal(1, repository.UpdateCallCount);
    }

    [Fact]
    public async Task Delete_WhenStorageThrows_StillSucceeds()
    {
        var user = CreateUser();
        var avatarUrl = UserAvatarMediaUrl.Build(user.Id, Guid.CreateVersion7(), "webp");
        Assert.True(user.SetAvatar(avatarUrl).IsSuccess);

        var repository = new FakeUserRepository();
        repository.Add(user);
        var storage = new FakePhotoStorage
        {
            DeleteException = new IOException("The file is locked.")
        };

        var result = await CreateHandler(repository, storage, user.Id).Handle(
            new DeleteUserAvatarCommand(),
            CancellationToken.None);

        // Строка в БД уже очищена: файл-сирота не должен превращать ответ в 500.
        Assert.True(result.IsSuccess);
        Assert.Equal(1, repository.UpdateCallCount);
    }

    [Fact]
    public async Task Delete_WithExternalAvatar_DoesNotCallStorage()
    {
        var user = CreateUser();
        Assert.True(user.SetAvatar("https://cdn.example.com/avatar.png").IsSuccess);

        var repository = new FakeUserRepository();
        repository.Add(user);
        var storage = new FakePhotoStorage();

        var result = await CreateHandler(repository, storage, user.Id).Handle(
            new DeleteUserAvatarCommand(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(user.AvatarUrl);
        Assert.Empty(storage.DeletedUrls);
    }

    [Fact]
    public async Task Delete_WhenAnonymous_ReturnsUnauthorized()
    {
        var user = CreateUser();
        var repository = new FakeUserRepository();
        repository.Add(user);
        var storage = new FakePhotoStorage();

        var result = await new DeleteUserAvatarHandler(
                repository,
                storage,
                FakeCurrentUser.Anonymous(),
                NullLogger<DeleteUserAvatarHandler>.Instance)
            .Handle(new DeleteUserAvatarCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.UNAUTHORIZED, Assert.Single(result.Error.Errors).Type);
        Assert.Equal(0, repository.GetByIdCallCount);
        Assert.Empty(storage.DeletedUrls);
    }

    private static DeleteUserAvatarHandler CreateHandler(
        FakeUserRepository repository,
        FakePhotoStorage storage,
        Guid userId)
        => new(
            repository,
            storage,
            new FakeCurrentUser(userId),
            NullLogger<DeleteUserAvatarHandler>.Instance);

    private static User CreateUser()
        => User.Create(
            $"avatar-{Guid.NewGuid():N}@barbersearch.test",
            "hash",
            "Ivan",
            "Petrov").Value;
}

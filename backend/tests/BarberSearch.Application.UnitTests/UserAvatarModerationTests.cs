using BarberSearch.Application.Users.Admin.BlockUserAvatar;
using BarberSearch.Application.Users.Admin.UnblockUserAvatar;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Административная модерация аватара: права, ошибки и идемпотентность обработчиков.
/// </summary>
/// <remarks>
/// Проверяется порядок решений обработчика, а не SQL: права — до чтения агрегата,
/// отсутствие пользователя — <c>404 user.notFound</c>, отсутствие аватара —
/// <c>409 user.blockAvatar.noAvatar</c>, успех — один вызов <c>UpdateAsync</c>.
/// </remarks>
public class UserAvatarModerationTests
{
    private const string AvatarUrl = "https://cdn.example.com/avatar.png";

    [Fact]
    public async Task Block_ByAdmin_BlocksAvatarAndSavesAggregateOnce()
    {
        var user = CreateUserWithAvatar();
        var repository = new FakeUserRepository();
        repository.Add(user);
        var handler = new BlockUserAvatarHandler(
            repository,
            new FakeCurrentUser(Guid.CreateVersion7(), isAdmin: true),
            NullLogger<BlockUserAvatarHandler>.Instance);

        var result = await handler.Handle(
            new BlockUserAvatarCommand(user.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(user.IsAvatarBlocked);
        Assert.Equal(AvatarUrl, user.AvatarUrl);
        Assert.Equal(1, repository.UpdateCallCount);
        Assert.Equal(1, repository.GetByIdCallCount);
    }

    [Fact]
    public async Task Block_ByOwner_ReturnsForbiddenWithoutReadingTheAggregate()
    {
        var ownerId = Guid.CreateVersion7();
        var user = CreateUserWithAvatar();
        var repository = new FakeUserRepository();
        repository.Add(user);
        var handler = new BlockUserAvatarHandler(
            repository,
            new FakeCurrentUser(ownerId),
            NullLogger<BlockUserAvatarHandler>.Instance);

        var result = await handler.Handle(
            new BlockUserAvatarCommand(user.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Equal("auth.forbidden", result.Error.Errors[0].Code);
        Assert.False(user.IsAvatarBlocked);
        Assert.Equal(0, repository.GetByIdCallCount);
        Assert.Equal(0, repository.UpdateCallCount);
    }

    [Fact]
    public async Task Block_Anonymous_ReturnsUnauthorized()
    {
        var user = CreateUserWithAvatar();
        var repository = new FakeUserRepository();
        repository.Add(user);
        var handler = new BlockUserAvatarHandler(
            repository,
            FakeCurrentUser.Anonymous(),
            NullLogger<BlockUserAvatarHandler>.Instance);

        var result = await handler.Handle(
            new BlockUserAvatarCommand(user.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.UNAUTHORIZED, result.Error.Errors[0].Type);
        Assert.Equal("auth.unauthorized", result.Error.Errors[0].Code);
        Assert.Equal(0, repository.GetByIdCallCount);
    }

    [Fact]
    public async Task Block_WhenUserMissing_ReturnsRepositoryNotFoundAsIs()
    {
        var repository = new FakeUserRepository();
        var handler = new BlockUserAvatarHandler(
            repository,
            new FakeCurrentUser(Guid.CreateVersion7(), isAdmin: true),
            NullLogger<BlockUserAvatarHandler>.Instance);

        var result = await handler.Handle(
            new BlockUserAvatarCommand(Guid.CreateVersion7()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
        Assert.Equal("user.notFound", result.Error.Errors[0].Code);
        Assert.Equal(0, repository.UpdateCallCount);
    }

    [Fact]
    public async Task Block_WhenAvatarIsMissing_ReturnsConflictWithoutSaving()
    {
        var user = User.Create("no-avatar@example.com", "hash", "Ivan", "Petrov").Value;
        var repository = new FakeUserRepository();
        repository.Add(user);
        var handler = new BlockUserAvatarHandler(
            repository,
            new FakeCurrentUser(Guid.CreateVersion7(), isAdmin: true),
            NullLogger<BlockUserAvatarHandler>.Instance);

        var result = await handler.Handle(
            new BlockUserAvatarCommand(user.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("user.blockAvatar.noAvatar", result.Error.Errors[0].Code);
        Assert.False(user.IsAvatarBlocked);
        Assert.Equal(0, repository.UpdateCallCount);
    }

    [Fact]
    public async Task Block_Repeated_SucceedsAndSavesOncePerCall()
    {
        var user = CreateUserWithAvatar();
        var repository = new FakeUserRepository();
        repository.Add(user);
        var handler = new BlockUserAvatarHandler(
            repository,
            new FakeCurrentUser(Guid.CreateVersion7(), isAdmin: true),
            NullLogger<BlockUserAvatarHandler>.Instance);

        var first = await handler.Handle(
            new BlockUserAvatarCommand(user.Id),
            CancellationToken.None);
        var updatedAt = user.UpdatedAt;
        var second = await handler.Handle(
            new BlockUserAvatarCommand(user.Id),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.True(user.IsAvatarBlocked);

        // Каждый вызов доходит до UpdateAsync (идемпотентная запись), но второй не меняет
        // UpdatedAt — значит, строка аудита не появится.
        Assert.Equal(2, repository.UpdateCallCount);
        Assert.Equal(updatedAt, user.UpdatedAt);
    }

    [Fact]
    public async Task Unblock_ByAdmin_ClearsAvatarFlagAndSavesAggregateOnce()
    {
        var user = CreateUserWithAvatar();
        Assert.True(user.BlockAvatar().IsSuccess);
        var repository = new FakeUserRepository();
        repository.Add(user);
        var handler = new UnblockUserAvatarHandler(
            repository,
            new FakeCurrentUser(Guid.CreateVersion7(), isAdmin: true),
            NullLogger<UnblockUserAvatarHandler>.Instance);

        var result = await handler.Handle(
            new UnblockUserAvatarCommand(user.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(user.IsAvatarBlocked);
        Assert.Equal(AvatarUrl, user.AvatarUrl);
        Assert.Equal(1, repository.UpdateCallCount);
    }

    [Fact]
    public async Task Unblock_ByOwner_ReturnsForbidden()
    {
        var ownerId = Guid.CreateVersion7();
        var user = CreateUserWithAvatar();
        Assert.True(user.BlockAvatar().IsSuccess);
        var repository = new FakeUserRepository();
        repository.Add(user);
        var handler = new UnblockUserAvatarHandler(
            repository,
            new FakeCurrentUser(ownerId),
            NullLogger<UnblockUserAvatarHandler>.Instance);

        var result = await handler.Handle(
            new UnblockUserAvatarCommand(user.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.True(user.IsAvatarBlocked);
        Assert.Equal(0, repository.UpdateCallCount);
    }

    [Fact]
    public async Task Unblock_WhenUserMissing_ReturnsNotFound()
    {
        var repository = new FakeUserRepository();
        var handler = new UnblockUserAvatarHandler(
            repository,
            new FakeCurrentUser(Guid.CreateVersion7(), isAdmin: true),
            NullLogger<UnblockUserAvatarHandler>.Instance);

        var result = await handler.Handle(
            new UnblockUserAvatarCommand(Guid.CreateVersion7()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
        Assert.Equal("user.notFound", result.Error.Errors[0].Code);
        Assert.Equal(0, repository.UpdateCallCount);
    }

    [Fact]
    public async Task Unblock_Repeated_SucceedsWithoutTouchingUpdatedAt()
    {
        var user = CreateUserWithAvatar();
        Assert.True(user.BlockAvatar().IsSuccess);
        var repository = new FakeUserRepository();
        repository.Add(user);
        var handler = new UnblockUserAvatarHandler(
            repository,
            new FakeCurrentUser(Guid.CreateVersion7(), isAdmin: true),
            NullLogger<UnblockUserAvatarHandler>.Instance);

        Assert.True((await handler.Handle(
            new UnblockUserAvatarCommand(user.Id),
            CancellationToken.None)).IsSuccess);

        var updatedAt = user.UpdatedAt;

        Assert.True((await handler.Handle(
            new UnblockUserAvatarCommand(user.Id),
            CancellationToken.None)).IsSuccess);

        Assert.False(user.IsAvatarBlocked);
        Assert.Equal(updatedAt, user.UpdatedAt);
        Assert.Equal(2, repository.UpdateCallCount);
    }

    private static User CreateUserWithAvatar()
    {
        var user = User.Create(
            $"{Guid.NewGuid():N}@example.com",
            "hash",
            "Ivan",
            "Petrov").Value;

        Assert.True(user.SetAvatar(AvatarUrl).IsSuccess);

        return user;
    }
}

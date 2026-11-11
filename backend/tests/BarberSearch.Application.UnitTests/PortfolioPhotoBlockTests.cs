using BarberSearch.Application.PortfolioPhotos.BlockPortfolioPhoto;
using BarberSearch.Application.PortfolioPhotos.UnblockPortfolioPhoto;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.BarberProfiles;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Административная блокировка фотографии: права, поиск фото и идемпотентность.
/// </summary>
public class PortfolioPhotoBlockTests
{
    [Fact]
    public async Task Block_ByAdmin_BlocksPhotoAndSavesAggregate()
    {
        var profile = CreateProfile(Guid.CreateVersion7());
        profile.AddPhoto("https://cdn/1.jpg");
        var photo = profile.PortfolioPhotos.Single();
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var handler = new BlockPortfolioPhotoHandler(
            repository,
            new FakeCurrentUser(Guid.CreateVersion7(), isAdmin: true),
            NullLogger<BlockPortfolioPhotoHandler>.Instance);

        var result = await handler.Handle(
            new BlockPortfolioPhotoCommand(profile.Id, photo.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(photo.IsBlocked);
        Assert.Equal(1, repository.UpdateCallCount);
    }

    [Fact]
    public async Task Block_ByOwner_ReturnsForbiddenWithoutLoadingProfile()
    {
        var ownerId = Guid.CreateVersion7();
        var profile = CreateProfile(ownerId);
        profile.AddPhoto("https://cdn/1.jpg");
        var photo = profile.PortfolioPhotos.Single();
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var handler = new BlockPortfolioPhotoHandler(
            repository,
            new FakeCurrentUser(ownerId),
            NullLogger<BlockPortfolioPhotoHandler>.Instance);

        var result = await handler.Handle(
            new BlockPortfolioPhotoCommand(profile.Id, photo.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Equal("auth.forbidden", result.Error.Errors[0].Code);
        Assert.False(photo.IsBlocked);
        Assert.Equal(0, repository.UpdateCallCount);
        Assert.Empty(repository.LoadCalls);
    }

    [Fact]
    public async Task Block_Anonymous_ReturnsUnauthorized()
    {
        var profile = CreateProfile(Guid.CreateVersion7());
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var handler = new BlockPortfolioPhotoHandler(
            repository,
            FakeCurrentUser.Anonymous(),
            NullLogger<BlockPortfolioPhotoHandler>.Instance);

        var result = await handler.Handle(
            new BlockPortfolioPhotoCommand(profile.Id, Guid.CreateVersion7()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.UNAUTHORIZED, result.Error.Errors[0].Type);
        Assert.Equal("auth.unauthorized", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Block_ForeignPhoto_ReturnsNotFound()
    {
        var profile = CreateProfile(Guid.CreateVersion7());
        profile.AddPhoto("https://cdn/1.jpg");
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var handler = new BlockPortfolioPhotoHandler(
            repository,
            new FakeCurrentUser(Guid.CreateVersion7(), isAdmin: true),
            NullLogger<BlockPortfolioPhotoHandler>.Instance);

        var result = await handler.Handle(
            new BlockPortfolioPhotoCommand(profile.Id, Guid.CreateVersion7()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
        Assert.Equal("portfolioPhoto.block.notFound", result.Error.Errors[0].Code);
        Assert.Equal(0, repository.UpdateCallCount);
    }

    [Fact]
    public async Task Block_Repeated_IsIdempotent()
    {
        var profile = CreateProfile(Guid.CreateVersion7());
        profile.AddPhoto("https://cdn/1.jpg");
        var photo = profile.PortfolioPhotos.Single();
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var handler = new BlockPortfolioPhotoHandler(
            repository,
            new FakeCurrentUser(Guid.CreateVersion7(), isAdmin: true),
            NullLogger<BlockPortfolioPhotoHandler>.Instance);

        var first = await handler.Handle(
            new BlockPortfolioPhotoCommand(profile.Id, photo.Id),
            CancellationToken.None);
        var second = await handler.Handle(
            new BlockPortfolioPhotoCommand(profile.Id, photo.Id),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.True(photo.IsBlocked);
    }

    [Fact]
    public async Task Unblock_ByAdmin_ClearsBlockAndSavesAggregate()
    {
        var profile = CreateProfile(Guid.CreateVersion7());
        profile.AddPhoto("https://cdn/1.jpg");
        var photo = profile.PortfolioPhotos.Single();
        photo.Block();
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var handler = new UnblockPortfolioPhotoHandler(
            repository,
            new FakeCurrentUser(Guid.CreateVersion7(), isAdmin: true),
            NullLogger<UnblockPortfolioPhotoHandler>.Instance);

        var result = await handler.Handle(
            new UnblockPortfolioPhotoCommand(profile.Id, photo.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(photo.IsBlocked);
        Assert.Equal(1, repository.UpdateCallCount);
    }

    [Fact]
    public async Task Unblock_ByOwner_ReturnsForbidden()
    {
        var ownerId = Guid.CreateVersion7();
        var profile = CreateProfile(ownerId);
        profile.AddPhoto("https://cdn/1.jpg");
        var photo = profile.PortfolioPhotos.Single();
        photo.Block();
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var handler = new UnblockPortfolioPhotoHandler(
            repository,
            new FakeCurrentUser(ownerId),
            NullLogger<UnblockPortfolioPhotoHandler>.Instance);

        var result = await handler.Handle(
            new UnblockPortfolioPhotoCommand(profile.Id, photo.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.True(photo.IsBlocked);
    }

    [Fact]
    public async Task Unblock_ForeignPhoto_ReturnsNotFound()
    {
        var profile = CreateProfile(Guid.CreateVersion7());
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var handler = new UnblockPortfolioPhotoHandler(
            repository,
            new FakeCurrentUser(Guid.CreateVersion7(), isAdmin: true),
            NullLogger<UnblockPortfolioPhotoHandler>.Instance);

        var result = await handler.Handle(
            new UnblockPortfolioPhotoCommand(profile.Id, Guid.CreateVersion7()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
        Assert.Equal("portfolioPhoto.unblock.notFound", result.Error.Errors[0].Code);
    }

    private static BarberProfile CreateProfile(Guid userId)
        => BarberProfile.Create(
            userId,
            "Master Barber",
            5,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null).Value;
}

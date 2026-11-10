using BarberSearch.Application.BarberProfiles.ReorderBarberProfilePhotos;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.BarberProfiles;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

public class ReorderBarberProfilePhotosHandlerTests
{
    [Fact]
    public async Task Reorder_ByOwner_AssignsOrderFromList()
    {
        var ownerId = Guid.CreateVersion7();
        var profile = CreateProfile(ownerId);
        profile.AddPhoto("https://cdn/1.jpg");
        profile.AddPhoto("https://cdn/2.jpg");
        profile.AddPhoto("https://cdn/3.jpg");
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var handler = new ReorderBarberProfilePhotosHandler(
            repository,
            new FakeCurrentUser(ownerId),
            NullLogger<ReorderBarberProfilePhotosHandler>.Instance);
        var photos = profile.PortfolioPhotos.ToList();
        var order = photos.Select(photo => photo.Id).Reverse().ToArray();

        var result = await handler.Handle(
            new ReorderBarberProfilePhotosCommand(profile.Id, order),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, repository.UpdateCallCount);

        for (var index = 0; index < order.Length; index++)
        {
            Assert.Equal(
                index,
                photos.Single(photo => photo.Id == order[index]).SortOrder);
        }
    }

    [Fact]
    public async Task Reorder_ByStranger_ReturnsForbidden()
    {
        var profile = CreateProfile(Guid.CreateVersion7());
        profile.AddPhoto("https://cdn/1.jpg");
        var photo = profile.PortfolioPhotos.Single();
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var handler = new ReorderBarberProfilePhotosHandler(
            repository,
            new FakeCurrentUser(Guid.CreateVersion7()),
            NullLogger<ReorderBarberProfilePhotosHandler>.Instance);

        var result = await handler.Handle(
            new ReorderBarberProfilePhotosCommand(profile.Id, [photo.Id]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Equal(0, repository.UpdateCallCount);
        Assert.Equal(0, photo.SortOrder);
    }

    [Fact]
    public async Task Reorder_WithIncompleteList_ReturnsValidation()
    {
        var ownerId = Guid.CreateVersion7();
        var profile = CreateProfile(ownerId);
        profile.AddPhoto("https://cdn/1.jpg");
        profile.AddPhoto("https://cdn/2.jpg");
        var first = profile.PortfolioPhotos.First();
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var handler = new ReorderBarberProfilePhotosHandler(
            repository,
            new FakeCurrentUser(ownerId),
            NullLogger<ReorderBarberProfilePhotosHandler>.Instance);

        var result = await handler.Handle(
            new ReorderBarberProfilePhotosCommand(profile.Id, [first.Id]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("barberProfile.reorderPhotos.invalid", result.Error.Errors[0].Code);
        Assert.Equal(0, repository.UpdateCallCount);
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

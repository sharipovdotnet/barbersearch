using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Application.Users.AddUserFavorite;
using BarberSearch.Application.Users.RemoveUserFavorite;
using BarberSearch.Application.Users.UpdateUserProfile;
using BarberSearch.Domain.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

public class UserOwnershipTests
{
    private static User CreateUser()
        => User.Create(
            $"user-{Guid.NewGuid():N}@example.com",
            "hash",
            "First",
            "Last").Value;

    [Fact]
    public async Task UpdateProfile_BySelf_Succeeds()
    {
        var user = CreateUser();
        var repository = new FakeUserRepository();
        repository.Add(user);
        var handler = new UpdateUserProfileHandler(
            repository,
            new FakeCurrentUser(user.Id),
            NullLogger<UpdateUserProfileHandler>.Instance);

        var result = await handler.Handle(
            new UpdateUserProfileCommand(user.Id, "Updated", "Name", "+70000000000", null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Updated", user.FirstName);
        Assert.Equal("+70000000000", user.PhoneNumber);
    }

    [Fact]
    public async Task UpdateProfile_ByStranger_ReturnsForbidden()
    {
        var user = CreateUser();
        var repository = new FakeUserRepository();
        repository.Add(user);
        var handler = new UpdateUserProfileHandler(
            repository,
            new FakeCurrentUser(Guid.CreateVersion7()),
            NullLogger<UpdateUserProfileHandler>.Instance);

        var result = await handler.Handle(
            new UpdateUserProfileCommand(user.Id, "Hacked", "Name", null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Equal("First", user.FirstName);
    }

    [Fact]
    public async Task UpdateProfile_ByAdmin_Succeeds()
    {
        var user = CreateUser();
        var repository = new FakeUserRepository();
        repository.Add(user);
        var handler = new UpdateUserProfileHandler(
            repository,
            new FakeCurrentUser(Guid.CreateVersion7(), isAdmin: true),
            NullLogger<UpdateUserProfileHandler>.Instance);

        var result = await handler.Handle(
            new UpdateUserProfileCommand(user.Id, "Admin", "Edit", null, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task AddFavorite_BySelf_AddsFavorite()
    {
        var user = CreateUser();
        var repository = new FakeUserRepository();
        repository.Add(user);
        var barberProfileId = Guid.CreateVersion7();
        var handler = new AddUserFavoriteHandler(
            repository,
            new FakeCurrentUser(user.Id),
            NullLogger<AddUserFavoriteHandler>.Instance);

        var result = await handler.Handle(
            new AddUserFavoriteCommand(user.Id, barberProfileId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var favorite = Assert.Single(user.FavoriteBarbers);
        Assert.Equal(barberProfileId, favorite.BarberProfileId);
    }

    [Fact]
    public async Task AddFavorite_ByStranger_ReturnsForbidden()
    {
        var user = CreateUser();
        var repository = new FakeUserRepository();
        repository.Add(user);
        var handler = new AddUserFavoriteHandler(
            repository,
            new FakeCurrentUser(Guid.CreateVersion7()),
            NullLogger<AddUserFavoriteHandler>.Instance);

        var result = await handler.Handle(
            new AddUserFavoriteCommand(user.Id, Guid.CreateVersion7()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Empty(user.FavoriteBarbers);
    }

    [Fact]
    public async Task RemoveFavorite_ByStranger_ReturnsForbidden()
    {
        var user = CreateUser();
        var barberProfileId = Guid.CreateVersion7();
        user.AddFavorite(barberProfileId);
        var repository = new FakeUserRepository();
        repository.Add(user);
        var handler = new RemoveUserFavoriteHandler(
            repository,
            new FakeCurrentUser(Guid.CreateVersion7()),
            NullLogger<RemoveUserFavoriteHandler>.Instance);

        var result = await handler.Handle(
            new RemoveUserFavoriteCommand(user.Id, barberProfileId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Single(user.FavoriteBarbers);
    }
}

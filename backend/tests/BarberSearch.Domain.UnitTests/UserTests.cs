using BarberSearch.Domain.Users;
using Xunit;

namespace BarberSearch.Domain.UnitTests;

public class UserTests
{
    private static User CreateValidUser(string email = "barber@example.com")
        => User.Create(email, "hash", "Ivan", "Petrov").Value;

    [Fact]
    public void Create_WithValidData_StartsAsActiveCustomer()
    {
        var result = User.Create("barber@example.com", "hash", "Ivan", "Petrov");

        Assert.True(result.IsSuccess);
        Assert.Equal(UserRole.Customer, result.Value.Role);
        Assert.True(result.Value.IsActive);
        Assert.Equal(Guid.Empty, result.Value.BarberProfileId ?? Guid.Empty);
    }

    [Fact]
    public void Create_WithBrokenEmail_ReturnsValidationFailure()
    {
        var result = User.Create("not-an-email", "hash", "Ivan", "Petrov");

        Assert.True(result.IsFailure);
        Assert.Contains("user.email", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Create_CollectsEveryErrorAtOnce()
    {
        var result = User.Create("", "", "", "");

        Assert.True(result.IsFailure);
        Assert.Equal(4, result.Error.Errors.Count);
    }

    [Fact]
    public void Create_WithUnknownRole_ReturnsValidationFailure()
    {
        var result = User.Create("barber@example.com", "hash", "Ivan", "Petrov", (UserRole)42);

        Assert.True(result.IsFailure);
        Assert.Equal("user.role", result.Error.Errors[0].Code);
    }

    [Fact]
    public void UpdateProfile_WithEmptyName_Fails()
    {
        var user = CreateValidUser();

        var result = user.UpdateProfile("", "Petrov", null, null);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void MarkPhoneAsVerified_RequiresPhoneNumber()
    {
        var user = CreateValidUser();

        var withoutPhone = user.MarkPhoneAsVerified();

        Assert.True(withoutPhone.IsFailure);
        Assert.False(user.IsPhoneVerified);

        user.UpdateProfile("Ivan", "Petrov", "+79001234567", null);

        var withPhone = user.MarkPhoneAsVerified();

        Assert.True(withPhone.IsSuccess);
        Assert.True(user.IsPhoneVerified);
    }

    [Fact]
    public void AddFavorite_ThenDuplicate_IsConflict()
    {
        var user = CreateValidUser();
        var barberProfileId = Guid.CreateVersion7();

        var first = user.AddFavorite(barberProfileId);
        var second = user.AddFavorite(barberProfileId);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsFailure);
        Assert.Equal("user.addFavorite.duplicate", second.Error.Errors[0].Code);
        Assert.Single(user.FavoriteBarbers);
    }

    [Fact]
    public void AddFavorite_WithDifferentBarbers_KeepsBoth()
    {
        var user = CreateValidUser();

        user.AddFavorite(Guid.CreateVersion7());
        user.AddFavorite(Guid.CreateVersion7());

        Assert.Equal(2, user.FavoriteBarbers.Count);
    }

    [Fact]
    public void RemoveFavorite_RemovesOnlyRequestedBarber()
    {
        var user = CreateValidUser();
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();

        user.AddFavorite(first);
        user.AddFavorite(second);

        var result = user.RemoveFavorite(first);

        Assert.True(result.IsSuccess);
        Assert.Single(user.FavoriteBarbers);
        Assert.Equal(second, user.FavoriteBarbers.First().BarberProfileId);
    }

    [Fact]
    public void RemoveFavorite_WhenMissing_ReturnsNotFound()
    {
        var user = CreateValidUser();

        var result = user.RemoveFavorite(Guid.CreateVersion7());

        Assert.True(result.IsFailure);
        Assert.Equal("user.removeFavorite.notFound", result.Error.Errors[0].Code);
    }

    [Fact]
    public void ChangeRole_WithUnknownValue_Fails()
    {
        var user = CreateValidUser();

        var result = user.ChangeRole((UserRole)99);

        Assert.True(result.IsFailure);
        Assert.Equal(UserRole.Customer, user.Role);
    }

    [Fact]
    public void ChangeRole_WithKnownValue_Succeeds()
    {
        var user = CreateValidUser();

        var result = user.ChangeRole(UserRole.Barber);

        Assert.True(result.IsSuccess);
        Assert.Equal(UserRole.Barber, user.Role);
    }

    [Fact]
    public void DeactivateThenActivate_RestoresState()
    {
        var user = CreateValidUser();

        user.Deactivate();
        Assert.False(user.IsActive);

        user.Activate();
        Assert.True(user.IsActive);
    }

    [Fact]
    public void RecordLogin_UpdatesLastLoginAt()
    {
        var user = CreateValidUser();

        Assert.Null(user.LastLoginAt);

        user.RecordLogin();

        Assert.NotNull(user.LastLoginAt);
    }

    [Fact]
    public void MarkEmailAsVerified_IsIdempotent()
    {
        var user = CreateValidUser();

        Assert.True(user.MarkEmailAsVerified().IsSuccess);
        Assert.True(user.MarkEmailAsVerified().IsSuccess);
        Assert.True(user.IsEmailVerified);
    }
}

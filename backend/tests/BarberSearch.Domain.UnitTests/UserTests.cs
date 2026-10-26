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
    public void Create_TrimsAndLowercasesEmail()
    {
        var result = User.Create("  User@Mail.RU  ", "hash", "Ivan", "Petrov");

        Assert.True(result.IsSuccess);
        Assert.Equal("user@mail.ru", result.Value.Email);
    }

    [Fact]
    public void Create_WithEmailWithoutDomain_ReturnsValidationFailure()
    {
        var result = User.Create("a@b", "hash", "Ivan", "Petrov");

        Assert.True(result.IsFailure);
        Assert.Equal("user.email", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Create_WithMultipleAtSigns_ReturnsValidationFailure()
    {
        var result = User.Create("a@b@c", "hash", "Ivan", "Petrov");

        Assert.True(result.IsFailure);
        Assert.Equal("user.email", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Create_WithDisplayName_ReturnsValidationFailure()
    {
        var result = User.Create("Ivan <ivan@example.com>", "hash", "Ivan", "Petrov");

        Assert.True(result.IsFailure);
        Assert.Equal("user.email", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Create_WithEmailLongerThanLimit_ReturnsValidationFailure()
    {
        var email = new string('a', 250) + "@example.com";

        var result = User.Create(email, "hash", "Ivan", "Petrov");

        Assert.True(result.IsFailure);
        Assert.Equal("user.email", result.Error.Errors[0].Code);
    }

    [Fact]
    public void UpdateProfile_WithEmptyName_Fails()
    {
        var user = CreateValidUser();

        var result = user.UpdateProfile("", "Petrov", null, null);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void UpdateProfile_RejectsInvalidAvatarUrlAndKeepsOldValue()
    {
        var user = CreateValidUser();
        user.UpdateProfile("Ivan", "Petrov", null, "https://cdn.example.com/avatar.png");

        foreach (var avatarUrl in new[]
                 {
                     "javascript:alert(1)",
                     "avatar.png",
                     "https://user:pass@cdn.example.com/avatar.png"
                 })
        {
            var result = user.UpdateProfile("Changed", "Petrov", "+79001234567", avatarUrl);

            Assert.True(result.IsFailure);

            var error = Assert.Single(result.Error.Errors);

            Assert.Equal("user.updateProfile.avatarUrl", error.Code);
            Assert.Equal("avatarUrl", error.InvalidField);
            Assert.Equal("Ivan", user.FirstName);
            Assert.Equal("https://cdn.example.com/avatar.png", user.AvatarUrl);
        }
    }

    [Fact]
    public void UpdateProfile_TrimsAvatarUrlAndClearsItOnBlank()
    {
        var user = CreateValidUser();

        var updated = user.UpdateProfile(
            "Ivan",
            "Petrov",
            null,
            "  https://cdn.example.com/avatar.png  ");

        Assert.True(updated.IsSuccess);
        Assert.Equal("https://cdn.example.com/avatar.png", user.AvatarUrl);

        var cleared = user.UpdateProfile("Ivan", "Petrov", null, "   ");

        Assert.True(cleared.IsSuccess);
        Assert.Null(user.AvatarUrl);
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

    [Fact]
    public void ChangePasswordHash_WithValidHash_ReplacesIt()
    {
        var user = CreateValidUser();

        var result = user.ChangePasswordHash("new-hash");

        Assert.True(result.IsSuccess);
        Assert.Equal("new-hash", user.PasswordHash);
    }

    [Fact]
    public void ChangePasswordHash_WithEmptyHash_KeepsOldOne()
    {
        var user = CreateValidUser();

        var result = user.ChangePasswordHash("   ");

        Assert.True(result.IsFailure);
        Assert.Equal("user.changePasswordHash", result.Error.Errors[0].Code);
        Assert.Equal("hash", user.PasswordHash);
    }

    [Fact]
    public void Create_InitializesNonEmptySecurityStamp()
    {
        var user = CreateValidUser();

        Assert.False(string.IsNullOrWhiteSpace(user.SecurityStamp));
    }

    [Fact]
    public void Create_GivesEveryUserItsOwnSecurityStamp()
    {
        var first = CreateValidUser("first@example.com");
        var second = CreateValidUser("second@example.com");

        Assert.NotEqual(first.SecurityStamp, second.SecurityStamp);
    }

    [Fact]
    public void RotateSecurityStamp_ReplacesTheValue()
    {
        var user = CreateValidUser();
        var before = user.SecurityStamp;

        user.RotateSecurityStamp();

        Assert.NotEqual(before, user.SecurityStamp);
        Assert.False(string.IsNullOrWhiteSpace(user.SecurityStamp));
    }

    [Fact]
    public void ChangePasswordHash_RotatesSecurityStamp()
    {
        var user = CreateValidUser();
        var before = user.SecurityStamp;

        var result = user.ChangePasswordHash("new-hash");

        Assert.True(result.IsSuccess);
        Assert.NotEqual(before, user.SecurityStamp);
    }

    [Fact]
    public void ChangePasswordHash_WithEmptyHash_KeepsSecurityStamp()
    {
        var user = CreateValidUser();
        var before = user.SecurityStamp;

        var result = user.ChangePasswordHash("   ");

        Assert.True(result.IsFailure);
        Assert.Equal(before, user.SecurityStamp);
    }

    [Fact]
    public void ChangeRole_RotatesSecurityStamp()
    {
        var user = CreateValidUser();
        var before = user.SecurityStamp;

        var result = user.ChangeRole(UserRole.Barber);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(before, user.SecurityStamp);
    }

    [Fact]
    public void ChangeRole_WithUnknownValue_KeepsSecurityStamp()
    {
        var user = CreateValidUser();
        var before = user.SecurityStamp;

        var result = user.ChangeRole((UserRole)99);

        Assert.True(result.IsFailure);
        Assert.Equal(before, user.SecurityStamp);
    }

    [Fact]
    public void Deactivate_RotatesSecurityStamp()
    {
        var user = CreateValidUser();
        var before = user.SecurityStamp;

        var result = user.Deactivate();

        Assert.True(result.IsSuccess);
        Assert.NotEqual(before, user.SecurityStamp);
    }

    [Fact]
    public void Deactivate_WhenAlreadyInactive_KeepsSecurityStamp()
    {
        var user = CreateValidUser();

        user.Deactivate();
        var afterFirst = user.SecurityStamp;

        var repeated = user.Deactivate();

        Assert.True(repeated.IsSuccess);
        Assert.Equal(afterFirst, user.SecurityStamp);
    }

    [Fact]
    public void RegisterFailedLogin_FourFailures_DoNotLockTheAccount()
    {
        var user = CreateValidUser();
        var now = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            now = now.AddSeconds(10);
            Assert.True(user.RegisterFailedLogin(now).IsSuccess);
        }

        Assert.Equal(4, user.FailedLoginAttempts);
        Assert.Equal(now, user.LastFailedLoginAt);
        Assert.Null(user.LockoutEnd);
        Assert.False(user.IsLockedOut(now));
    }

    [Fact]
    public void RegisterFailedLogin_FifthFailure_LocksForExactlyOneMinute()
    {
        var user = CreateValidUser();
        var now = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

        for (var attempt = 1; attempt <= Constants.MAX_FAILED_LOGIN_ATTEMPTS; attempt++)
        {
            now = now.AddSeconds(10);
            user.RegisterFailedLogin(now);
        }

        var failedAt = now;

        Assert.Equal(Constants.MAX_FAILED_LOGIN_ATTEMPTS, user.FailedLoginAttempts);
        Assert.Equal(failedAt.AddMinutes(Constants.LOGIN_LOCKOUT_BASE_MINUTES), user.LockoutEnd);
        Assert.True(user.IsLockedOut(failedAt));
        Assert.True(user.IsLockedOut(failedAt.AddMinutes(1).AddMilliseconds(-1)));

        // В момент окончания задержки аккаунт уже не заблокирован, иначе клиент,
        // дождавшийся конца блокировки, получал бы ещё один отказ.
        Assert.False(user.IsLockedOut(user.LockoutEnd!.Value));
    }

    [Fact]
    public void RegisterFailedLogin_SixthFailureAfterLockoutExpires_LocksForTwoMinutes()
    {
        var user = CreateValidUser();
        var now = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

        for (var attempt = 1; attempt <= Constants.MAX_FAILED_LOGIN_ATTEMPTS; attempt++)
        {
            now = now.AddSeconds(10);
            user.RegisterFailedLogin(now);
        }

        var lockoutEnd = user.LockoutEnd!.Value;

        Assert.False(user.IsLockedOut(lockoutEnd));

        now = lockoutEnd;
        user.RegisterFailedLogin(now);

        Assert.Equal(Constants.MAX_FAILED_LOGIN_ATTEMPTS + 1, user.FailedLoginAttempts);
        Assert.Equal(now.AddMinutes(2), user.LockoutEnd);
        Assert.True(user.IsLockedOut(now));
        Assert.True(user.IsLockedOut(now.AddSeconds(1)));
    }

    [Fact]
    public void RegisterFailedLogin_GrowsBackoffUpToMaximum()
    {
        var user = CreateValidUser();
        var now = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

        for (var attempt = 1; attempt <= Constants.MAX_FAILED_LOGIN_ATTEMPTS; attempt++)
        {
            now = now.AddSeconds(10);
            user.RegisterFailedLogin(now);
        }

        // 6-я неудача — 2 минуты, 7-я — 4, 8-я — 8. Дальше пауза между неудачами
        // превысила бы окно сброса, поэтому рост до потолка проверяется отдельно.
        foreach (var expectedMinutes in new[] { 2, 4, 8 })
        {
            now = user.LockoutEnd!.Value;
            user.RegisterFailedLogin(now);

            Assert.Equal(now.AddMinutes(expectedMinutes), user.LockoutEnd);
        }

        Assert.Equal(Constants.MAX_FAILED_LOGIN_ATTEMPTS + 3, user.FailedLoginAttempts);
    }

    [Fact]
    public void RegisterFailedLogin_CapsCounterAndLockoutDelay()
    {
        var user = CreateValidUser();
        var now = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

        // Сотня неудач подряд внутри окна сброса: счётчик упирается в предохранитель,
        // а задержка — в LOGIN_LOCKOUT_MAX_MINUTES, без переполнения int.
        for (var attempt = 1; attempt <= 100; attempt++)
        {
            now = now.AddSeconds(1);
            user.RegisterFailedLogin(now);
        }

        Assert.Equal(
            Constants.MAX_FAILED_LOGIN_ATTEMPTS + 10,
            user.FailedLoginAttempts);
        Assert.Equal(
            now.AddMinutes(Constants.LOGIN_LOCKOUT_MAX_MINUTES),
            user.LockoutEnd);
        Assert.True(user.IsLockedOut(now.AddMinutes(59)));
        Assert.False(user.IsLockedOut(now.AddMinutes(60)));
    }

    [Fact]
    public void ResetFailedLogin_ClearsCounterAndLockoutAndIsIdempotent()
    {
        var user = CreateValidUser();
        var now = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

        for (var attempt = 1; attempt <= Constants.MAX_FAILED_LOGIN_ATTEMPTS; attempt++)
        {
            now = now.AddSeconds(10);
            user.RegisterFailedLogin(now);
        }

        Assert.True(user.IsLockedOut(now));

        var reset = user.ResetFailedLogin();

        Assert.True(reset.IsSuccess);
        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LastFailedLoginAt);
        Assert.Null(user.LockoutEnd);
        Assert.False(user.IsLockedOut(now));

        // Повторный сброс идемпотентен: состояние остаётся пустым.
        Assert.True(user.ResetFailedLogin().IsSuccess);
        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LockoutEnd);
    }

    [Fact]
    public void RegisterFailedLogin_AfterResetWindow_StartsCountingFromScratch()
    {
        var user = CreateValidUser();
        var now = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            now = now.AddSeconds(10);
            user.RegisterFailedLogin(now);
        }

        Assert.Equal(4, user.FailedLoginAttempts);

        // Пауза больше окна сброса: прошлые неудачи больше не считаются.
        var afterPause = now.AddMinutes(Constants.LOGIN_FAILURE_RESET_MINUTES + 1);
        user.RegisterFailedLogin(afterPause);

        Assert.Equal(1, user.FailedLoginAttempts);
        Assert.Equal(afterPause, user.LastFailedLoginAt);
        Assert.Null(user.LockoutEnd);
        Assert.False(user.IsLockedOut(afterPause));
    }
}

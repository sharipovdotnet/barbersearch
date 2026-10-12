using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.Common;
using BarberSearch.Domain.FavoriteBarbers;

namespace BarberSearch.Domain.Users;

public class User : AggregateRoot
{
    private readonly List<FavoriteBarber> _favoriteBarbers = new();

    public string Email { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;

    public string FirstName { get; private set; } = null!;
    public string LastName { get; private set; } = null!;

    public string? PhoneNumber { get; private set; }
    public string? AvatarUrl { get; private set; }

    public bool IsEmailVerified { get; private set; }
    public bool IsPhoneVerified { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public BarberProfile? BarberProfile { get; private set; }

    public IReadOnlyCollection<FavoriteBarber> FavoriteBarbers
        => _favoriteBarbers.AsReadOnly();

    private User()
    {
    }

    public User(
        string email,
        string passwordHash,
        string firstName,
        string lastName)
    {
        Email = email;
        PasswordHash = passwordHash;
        FirstName = firstName;
        LastName = lastName;
        IsActive = true;
    }

    public void UpdateProfile(
        string firstName,
        string lastName,
        string? phoneNumber,
        string? avatarUrl)
    {
        FirstName = firstName;
        LastName = lastName;
        PhoneNumber = phoneNumber;
        AvatarUrl = avatarUrl;

        MarkUpdated();
    }

    public void MarkEmailAsVerified()
    {
        IsEmailVerified = true;
        MarkUpdated();
    }

    public void RecordLogin()
    {
        LastLoginAt = DateTimeOffset.UtcNow;
        MarkUpdated();
    }

    public void Deactivate()
    {
        IsActive = false;
        MarkUpdated();
    }
}
using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// Текущий пользователь запроса, заданный тестом: идентификатор из «токена» и признак admin.
/// </summary>
public sealed class FakeCurrentUser : ICurrentUser
{
    public FakeCurrentUser(Guid userId, bool isAdmin = false)
    {
        UserId = userId;
        IsAdmin = isAdmin;
    }

    public Guid UserId { get; }

    public bool IsAdmin { get; }
}

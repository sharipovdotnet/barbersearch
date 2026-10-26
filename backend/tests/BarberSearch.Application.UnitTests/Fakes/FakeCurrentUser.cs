using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// Текущий пользователь запроса, заданный тестом: идентификатор из «токена» и признак admin.
/// </summary>
/// <remarks>
/// <see cref="Anonymous"/> создаёт состояние анонимного запроса: <see cref="TryGetUserId"/>
/// возвращает <c>false</c>, как и настоящий <c>HttpContextCurrentUser</c> без claim'а <c>sub</c>.
/// </remarks>
public sealed class FakeCurrentUser : ICurrentUser
{
    private readonly Guid? _userId;

    public FakeCurrentUser(Guid userId, bool isAdmin = false)
    {
        _userId = userId;
        IsAdmin = isAdmin;
    }

    private FakeCurrentUser(bool isAdmin)
    {
        _userId = null;
        IsAdmin = isAdmin;
    }

    /// <summary>
    /// Анонимный запрос без claim'а <c>sub</c>.
    /// </summary>
    public static FakeCurrentUser Anonymous(bool isAdmin = false) => new(isAdmin);

    public bool TryGetUserId(out Guid userId)
    {
        if (_userId is { } id)
        {
            userId = id;

            return true;
        }

        userId = Guid.Empty;

        return false;
    }

    public bool IsAdmin { get; }
}

using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// Текущий пользователь запроса, заданный тестом: идентификатор из «токена», признак admin
/// и профиль барбера из claim'а <c>barber_profile_id</c>.
/// </summary>
/// <remarks>
/// <see cref="Anonymous"/> создаёт состояние анонимного запроса: <see cref="TryGetUserId"/>
/// возвращает <c>false</c>, как и настоящий <c>HttpContextCurrentUser</c> без claim'а <c>sub</c>.
/// Профиль барбера по умолчанию не задан: роль <c>Barber</c> и привязанный профиль —
/// разные вещи, и сервер обязан закрыть случай «роль есть, claim'а нет».
/// </remarks>
public sealed class FakeCurrentUser : ICurrentUser
{
    private readonly Guid? _userId;
    private readonly Guid? _barberProfileId;

    public FakeCurrentUser(Guid userId, bool isAdmin = false, Guid? barberProfileId = null)
    {
        _userId = userId;
        _barberProfileId = barberProfileId;
        IsAdmin = isAdmin;
    }

    private FakeCurrentUser(bool isAdmin)
    {
        _userId = null;
        _barberProfileId = null;
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

    public bool TryGetBarberProfileId(out Guid barberProfileId)
    {
        if (_barberProfileId is { } id)
        {
            barberProfileId = id;

            return true;
        }

        barberProfileId = Guid.Empty;

        return false;
    }

    public bool IsAdmin { get; }
}

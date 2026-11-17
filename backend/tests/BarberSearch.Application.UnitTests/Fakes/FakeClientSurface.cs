using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="IClientSurface"/>: тест задаёт поверхность запроса явно.
/// </summary>
/// <remarks>
/// Настоящая реализация читает заголовок <c>X-Client-Surface</c>, но для уведомителей важен
/// только признак <see cref="IsMobileApp"/>: здесь он изменяемый, чтобы один тест мог проверить
/// оба поведения без нового контекста. Значение по умолчанию — «веб»: уведомления создаются.
/// </remarks>
public sealed class FakeClientSurface : IClientSurface
{
    /// <inheritdoc />
    public bool IsMobileApp { get; set; }
}

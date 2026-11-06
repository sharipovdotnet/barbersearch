using BarberSearch.Domain.Salons;

namespace BarberSearch.Contracts.Salons.CreateSalon;

/// <summary>
/// Данные запроса на создание салона.
/// </summary>
/// <remarks>
/// Контракт слоя Application: команда <c>CreateSalonCommand</c> несёт его целиком, а не
/// десять позиционных параметров, поэтому поля невозможно перепутать местами при передаче
/// в <c>Salon.Create</c>. Обязательность и формат полей проверяет домен — здесь только состав.
/// <para>
/// <see cref="Kind"/> и <see cref="IsListed"/> необязательны и по умолчанию повторяют прежнее
/// поведение (<c>Salon</c>, <c>true</c>): старый клиент, не приславший их, создаёт обычный
/// барбершоп, который проходит модерацию. Фронтенд присылает оба поля явно — серверный
/// дефолт нужен только для совместимости. Тип места приходит именем перечисления (числа
/// запрещены настройками JSON, R-23), а совместимость типа и видимости проверяет домен
/// (<c>salon.kind.unknown</c> / <c>salon.isListed.kind</c>).
/// </para>
/// </remarks>
/// <param name="Kind">Вид места приёма; по умолчанию обычный барбершоп.</param>
/// <param name="IsListed">Показывать ли место в каталоге; по умолчанию <c>true</c>.</param>
public record CreateSalonRequest(
    string Name,
    string Address,
    string City,
    string Country,
    string TimeZoneId,
    double Latitude,
    double Longitude,
    string? PostalCode,
    string? PhoneNumber,
    string? WebsiteUrl,
    PlaceKind Kind = PlaceKind.Salon,
    bool IsListed = true);

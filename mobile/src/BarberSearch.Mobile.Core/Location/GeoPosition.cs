namespace BarberSearch.Mobile.Core.Location;

/// <summary>
/// Географическая точка устройства: широта и долгота в градусах WGS 84.
/// </summary>
/// <remarks>
/// Тип живёт в Core, чтобы ViewModel поиска не зависел от MAUI и проверялся обычным xunit.
/// Платформенный <c>Location</c> в Core не попадает: он тянет за собой <c>Microsoft.Maui.*</c>.
/// </remarks>
/// <param name="Latitude">Широта, −90..90.</param>
/// <param name="Longitude">Долгота, −180..180.</param>
public readonly record struct GeoPosition(double Latitude, double Longitude);

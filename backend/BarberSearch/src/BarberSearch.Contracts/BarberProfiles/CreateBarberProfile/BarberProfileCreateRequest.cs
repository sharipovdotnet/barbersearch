using BarberSearch.Domain.BarberProfiles.Enums;

namespace BarberSearch.Contracts.BarberProfiles.CreateBarberProfile;

/// <summary>
/// Данные запроса на создание профиля барбера.
/// </summary>
/// <remarks>
/// Контракт слоя Application: команда <c>CreateBarberProfileCommand</c> несёт его целиком,
/// а домен получает значения — <c>BarberProfile.Create</c> принимает их явными параметрами,
/// потому что на слой Contracts он не ссылается. Владелец профиля в запросе не передаётся:
/// это текущий пользователь из токена, поэтому завести профиль на чужой идентификатор нельзя.
/// Enum'ы (<see cref="Language"/>, <see cref="PaymentMethod"/>) приходят из домена: это словарь
/// предметной области, а не деталь транспорта.
/// </remarks>
public record BarberProfileCreateRequest(
    string DisplayName,
    int YearsOfExperience,
    string? Bio,
    List<Language> Languages,
    List<string>? CertificatesAndTraining,
    List<string>? Achievements,
    List<PaymentMethod> PaymentMethods,
    List<string>? CommunicationFormat,
    List<string>? Goodies,
    string? LatePolicy);

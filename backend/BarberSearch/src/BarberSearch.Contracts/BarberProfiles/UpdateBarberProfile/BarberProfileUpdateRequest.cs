using BarberSearch.Domain.BarberProfiles.Enums;

namespace BarberSearch.Contracts.BarberProfiles.UpdateBarberProfile;

/// <summary>
/// Данные запроса на обновление профиля барбера.
/// </summary>
/// <remarks>
/// Контракт слоя Application: команда <c>UpdateBarberProfileCommand</c> несёт его целиком,
/// а домен получает значения — <c>BarberProfile.UpdateProfile</c> принимает их явными
/// параметрами, потому что на слой Contracts он не ссылается.
/// Грейда здесь нет: <c>Grade</c> меняется только административным сценарием
/// (<c>BarberProfile.ChangeGrade</c>). <see cref="Bio"/> nullable, чтобы описание можно было очистить.
/// </remarks>
public record BarberProfileUpdateRequest(
    string DisplayName,
    int YearsOfExperience,
    string? Bio,
    List<Language> Languages,
    List<string> CertificatesAndTraining,
    List<string> Achievements,
    List<PaymentMethod> PaymentMethods,
    List<string> CommunicationFormat,
    List<string> Goodies,
    string LatePolicy);

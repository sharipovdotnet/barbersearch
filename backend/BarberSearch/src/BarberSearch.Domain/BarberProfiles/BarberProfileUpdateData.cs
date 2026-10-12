using BarberSearch.Domain.BarberProfiles.Enums;

namespace BarberSearch.Domain.BarberProfiles;

/// <summary>
/// Данные для обновления профиля барбера.
/// </summary>
/// <remarks>
/// Грейда здесь нет: <c>Grade</c> меняется только административным сценарием
/// (<c>BarberProfile.ChangeGrade</c>). <see cref="Bio"/> nullable, чтобы описание можно было очистить.
/// </remarks>
public record BarberProfileUpdateData(
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

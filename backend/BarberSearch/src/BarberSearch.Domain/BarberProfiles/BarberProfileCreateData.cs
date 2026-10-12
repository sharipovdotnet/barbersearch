using BarberSearch.Domain.BarberProfiles.Enums;

namespace BarberSearch.Domain.BarberProfiles;

public record BarberProfileCreateData(
    Guid UserId,
    string DisplayName,
    int YearsOfExperience,
    string? Bio,
    List<Language> Language,
    List<string>? CertificatesAndTraining,
    List<string>? Achievements,
    List<PaymentMethod> PaymentMethods,
    List<string>? CommunicationFormat,
    List<string>? Goodies,
    string? LatePolicy);
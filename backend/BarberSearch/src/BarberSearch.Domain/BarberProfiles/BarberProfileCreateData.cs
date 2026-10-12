namespace BarberSearch.Domain.BarberProfiles;

public record BarberProfileCreationData(
    Guid UserId,
    string DisplayName,
    int YearsOfExperience,
    string? Status,
    string? Bio,
    string? Language,
    string? CertificatesAndTraining,
    string? Achievements,
    string? PaymentMethods,
    string? CommunicationFormat,
    string? Goodies,
    string? LatePolicy);
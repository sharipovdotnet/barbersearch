using System;
using System.Collections.Generic;
using BarberSearch.Domain.BarberProfiles.Enums;

namespace BarberSearch.Domain.BarberProfiles;

/// <summary>
/// Данные для создания профиля барбера.
/// </summary>
public record BarberProfileCreateData(
    Guid UserId,
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

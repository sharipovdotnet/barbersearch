using BarberSearch.Domain.BarberProfiles.Enums;

namespace BarberSearch.Contracts.BarberProfiles.GetBarberProfile;

/// <summary>
/// Карточка барбера для клиента: профиль, услуги, салоны и специализации.
/// </summary>
/// <remarks>
/// Доменный агрегат наружу не сериализуется: у дочерних сущностей есть навигация
/// на профиль, и сериализация замкнула бы граф в цикл. Все вложенные коллекции —
/// отдельные response-записи без обратных ссылок.
/// </remarks>
/// <param name="Id">Идентификатор профиля барбера.</param>
/// <param name="UserId">Идентификатор пользователя-владельца профиля.</param>
/// <param name="DisplayName">Отображаемое имя мастера.</param>
/// <param name="Grade">Грейд барбера.</param>
/// <param name="Bio">Короткое описание о себе.</param>
/// <param name="YearsOfExperience">Стаж в годах.</param>
/// <param name="Languages">Языки общения.</param>
/// <param name="CertificatesAndTraining">Сертификаты и обучение.</param>
/// <param name="Rating">Средний рейтинг в шкале 1.00..5.00 либо <c>null</c>, если отзывов нет.</param>
/// <param name="ReviewsCount">Количество опубликованных отзывов.</param>
/// <param name="Achievements">Достижения мастера.</param>
/// <param name="PaymentMethods">Способы оплаты.</param>
/// <param name="CommunicationFormat">Предпочтительный формат общения.</param>
/// <param name="Goodies">Комфорт в кресле.</param>
/// <param name="LatePolicy">Правило опозданий.</param>
/// <param name="IsActive">Активен ли профиль.</param>
/// <param name="IsVerified">Подтверждён ли профиль администратором.</param>
/// <param name="Services">Активные услуги барбера с ценами и длительностью.</param>
/// <param name="Salons">Активные салоны барбера вместе с расписанием пары (барбер, салон).</param>
/// <param name="Specializations">Специализации барбера из справочника.</param>
public record BarberProfileResponse(
    Guid Id,
    Guid UserId,
    string DisplayName,
    Grade Grade,
    string? Bio,
    int YearsOfExperience,
    IReadOnlyList<Language> Languages,
    IReadOnlyList<string> CertificatesAndTraining,
    decimal? Rating,
    int ReviewsCount,
    IReadOnlyList<string> Achievements,
    IReadOnlyList<PaymentMethod> PaymentMethods,
    IReadOnlyList<string> CommunicationFormat,
    IReadOnlyList<string> Goodies,
    string LatePolicy,
    bool IsActive,
    bool IsVerified,
    IReadOnlyList<BarberProfileServiceResponse> Services,
    IReadOnlyList<BarberProfileSalonResponse> Salons,
    IReadOnlyList<BarberProfileSpecializationResponse> Specializations);

namespace BarberSearch.Contracts.BarberRequests.CreateBarberRequest;

/// <summary>
/// Данные запроса на создание заявки в барберы.
/// </summary>
/// <remarks>
/// Роль в теле не передаётся: заявка не выдаёт права, а роль <c>Barber</c> появляется только
/// после одобрения администратором. Состав полей совпадает с моделью заявки, поэтому команда
/// несёт запись целиком, а обязательность и форматы проверяет домен.
/// </remarks>
/// <param name="DisplayName">Имя, под которым заявитель хочет работать; 1..120 символов.</param>
/// <param name="YearsOfExperience">Опыт работы; 1..70 лет.</param>
/// <param name="City">Город; 1..100 символов.</param>
/// <param name="About">Рассказ о себе; <c>null</c> или до 2000 символов.</param>
/// <param name="HasSalon">Уже работает в салоне.</param>
public record CreateBarberRequestRequest(
    string DisplayName,
    int YearsOfExperience,
    string City,
    string? About,
    bool HasSalon);

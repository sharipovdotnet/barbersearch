namespace BarberSearch.Contracts.Reviews.Admin;

/// <summary>
/// Клиент — автор отзыва. Контактные данные доступны только администратору.
/// </summary>
/// <remarks>
/// Телефон сюда сознательно не входит: для модерации достаточно имени и email.
/// Контракт не используется публичной выдачей.
/// </remarks>
/// <param name="Id">Идентификатор пользователя.</param>
/// <param name="FullName">Имя и фамилия клиента.</param>
/// <param name="Email">Email клиента.</param>
public record AdminReviewCustomerResponse(
    Guid Id,
    string FullName,
    string Email);

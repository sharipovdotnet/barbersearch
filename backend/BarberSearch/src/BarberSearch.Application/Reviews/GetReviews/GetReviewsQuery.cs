using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Reviews.GetReviews;

namespace BarberSearch.Application.Reviews.GetReviews;

/// <summary>
/// Запрос опубликованных отзывов барбера с keyset-пагинацией.
/// </summary>
/// <remarks>
/// <paramref name="BarberProfileId"/> обязателен: без него выдача всех отзывов
/// не имеет смысла для карточки. Отсутствие параметра в HTTP-запросе отсекает model binding
/// контроллера; явный пустой GUID (<see cref="Guid.Empty"/>) отклоняет обработчик доменным
/// кодом <c>review.getReviews.barberProfileId</c> (defense-in-depth). Скрытые отзывы
/// в ответ не попадают.
/// </remarks>
/// <param name="BarberProfileId">Идентификатор профиля барбера.</param>
/// <param name="Cursor">Курсор следующей страницы.</param>
/// <param name="PageSize">Размер страницы; <c>null</c> — значение по умолчанию.</param>
public record GetReviewsQuery(
    Guid BarberProfileId,
    string? Cursor,
    int? PageSize) : IQuery<PagedResponse<ReviewResponse>>;

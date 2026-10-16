namespace BarberSearch.Application.Reviews;

/// <summary>
/// Сводка по опубликованным отзывам барбера: средняя оценка и количество отзывов.
/// </summary>
/// <remarks>
/// Ровно те два значения, которые нужны <c>BarberProfile.UpdateRating</c>:
/// <see cref="AverageRating"/> = <c>null</c> вместе с <see cref="Count"/> = 0 означает
/// «отзывов ещё нет». Округление делает домен, репозиторий возвращает среднее как есть.
/// </remarks>
/// <param name="AverageRating">Средняя оценка в шкале 1.00..5.00 либо <c>null</c>, если отзывов нет.</param>
/// <param name="Count">Количество опубликованных отзывов.</param>
public sealed record ReviewRatingSummary(decimal? AverageRating, int Count);

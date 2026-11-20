namespace BarberSearch.Mobile.Core.Navigation;

/// <summary>
/// Маршруты Shell для разделов администрирования.
/// </summary>
/// <remarks>
/// <para>
/// Строки собраны в одном месте, потому что их знают двое: страница, которая на них
/// регистрируется, и хаб, который по ним переходит. Веб держит адреса в
/// <c>frontend/src/lib/admin-path.ts</c> по той же причине — разошедшийся адрес даёт
/// «раздел не найден» вместо раздела.
/// </para>
/// <para>
/// Это не адреса веба: телефон открывает страницы Shell, а не пути браузера, поэтому
/// `admin/reviews/pending` превращается в пару «очередь + фильтр», которую раздел держит
/// в своём ViewModel. Константы живут в Core, а не рядом со страницами, потому что их
/// первым читает ViewModel хаба: он собирает строки разделов до того, как откроется страница.
/// </para>
/// </remarks>
public static class AdminRoutes
{
    /// <summary>
    /// Хаб администрирования: очереди модерации и состояние разделов.
    /// </summary>
    public const string Hub = "admin";

    /// <summary>
    /// Очередь отзывов на модерации.
    /// </summary>
    public const string Reviews = "admin-reviews";

    /// <summary>
    /// Карточка отзыва: текст, оценка и решение администратора.
    /// </summary>
    public const string Review = "admin-review";

    /// <summary>
    /// Очереди фотографий: аватары и портфолио.
    /// </summary>
    public const string Photos = "admin-photos";

    /// <summary>
    /// Очередь профилей мастеров без подтверждения.
    /// </summary>
    public const string Masters = "admin-masters";

    /// <summary>
    /// Карточка мастера: профиль и решение администратора.
    /// </summary>
    public const string Master = "admin-master";

    /// <summary>
    /// Очередь заявок «Стать мастером».
    /// </summary>
    public const string Requests = "admin-requests";

    /// <summary>
    /// Карточка заявки: анкета и решение администратора.
    /// </summary>
    public const string Request = "admin-request";
}

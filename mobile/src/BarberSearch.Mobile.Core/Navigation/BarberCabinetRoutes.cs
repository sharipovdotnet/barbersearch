namespace BarberSearch.Mobile.Core.Navigation;

/// <summary>
/// Маршруты Shell для кабинета мастера.
/// </summary>
/// <remarks>
/// <para>
/// Строки собраны в одном месте по тому же правилу, что <see cref="AdminRoutes"/>: их знают
/// двое — страница, которая регистрируется, и раздел кабинета, который по ним переходит.
/// Веб держит свои адреса в <c>frontend/src/lib/barber-cabinet-path.ts</c>; телефон открывает
/// страницы Shell, поэтому адреса — короткие имена маршрутов, а не пути браузера.
/// </para>
/// <para>
/// Кабинет — один экран с четырьмя разделами (записи, расписание, салоны, профиль), а не
/// четыре вкладки: роль мастера редкая, и пятая вкладка нижней полосы занимала бы место
/// у всех остальных. Поэтому разделы переключаются внутри страницы кабинета, и маршрут
/// у кабинета один. Отдельные маршруты есть только у экранов, которые открываются поверх
/// кабинета: форма записи, перенос, заметка, онбординг, уведомления, правка профиля и услуги.
/// </para>
/// </remarks>
public static class BarberCabinetRoutes
{
    /// <summary>
    /// Кабинет мастера: четыре раздела и шапка с колокольчиком.
    /// </summary>
    public const string Cabinet = "barber-cabinet";

    /// <summary>
    /// Создание записи клиента вручную.
    /// </summary>
    public const string CreateBooking = "barber-booking-create";

    /// <summary>
    /// Перенос визита клиента.
    /// </summary>
    public const string RescheduleBooking = "barber-booking-reschedule";

    /// <summary>
    /// Заметка мастера к визиту.
    /// </summary>
    public const string BookingNote = "barber-booking-note";

    /// <summary>
    /// Создание профиля мастера и первого салона.
    /// </summary>
    public const string Onboarding = "barber-onboarding";

    /// <summary>
    /// Инбокс уведомлений мастера.
    /// </summary>
    public const string Notifications = "barber-notifications";

    /// <summary>
    /// Правка публичной карточки мастера.
    /// </summary>
    public const string EditProfile = "barber-profile-edit";

    /// <summary>
    /// Прайс: добавление услуги или правка цены и длительности.
    /// </summary>
    public const string ServiceEditor = "barber-service-editor";
}

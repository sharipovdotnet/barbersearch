namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Раздел кабинета мастера, который живёт внутри одной страницы.
/// </summary>
/// <remarks>
/// <para>
/// Кабинет — не четыре вкладки и не четыре страницы: роль мастера редкая, и отдельная вкладка
/// занимала бы место у всех. Четыре раздела переключаются внутри <c>BarberCabinetPage</c>,
/// поэтому у раздела нет собственного жизненного цикла страницы: страница кабинета сообщает
/// ему, когда он показан и когда скрыт.
/// </para>
/// <para>
/// <see cref="Initialize"/> вызывается один раз, когда страница узнала профиль мастера из
/// <c>GET api/auth/me</c>; <see cref="ActivateAsync"/> — каждый раз, когда раздел становится
/// видимым, и после возврата с дочернего экрана (создание записи, правка профиля): данные
/// могли измениться. <see cref="Deactivate"/> обязателен перед переходом на дочерний экран:
/// Shell не поднимает <c>OnDisappearing</c> у страницы, поверх которой открыли следующую,
/// и цикл автоповтора иначе продолжил бы ходить в сеть под чужой страницей.
/// </para>
/// </remarks>
public interface IBarberCabinetSection
{
    /// <summary>
    /// Идентификатор профиля мастера, которому принадлежит раздел; <c>null</c> до инициализации.
    /// </summary>
    Guid? BarberProfileId { get; }

    /// <summary>
    /// Привязывает раздел к профилю мастера.
    /// </summary>
    /// <param name="barberProfileId">Профиль из текущей сессии.</param>
    void Initialize(Guid barberProfileId);

    /// <summary>
    /// Показывает раздел: первое чтение или тихое обновление, если данные уже есть.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача чтения.</returns>
    Task ActivateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Скрывает раздел: останавливает автоповтор и таймеры, не сбрасывая данные.
    /// </summary>
    void Deactivate();
}

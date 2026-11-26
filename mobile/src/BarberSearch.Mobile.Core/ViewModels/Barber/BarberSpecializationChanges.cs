namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Разница между специализациями активных услуг мастера и тегами его профиля.
/// </summary>
/// <remarks>
/// Списки уже очищены от повторов и сохраняют порядок первого появления: добавление — в порядке
/// услуг, удаление — в порядке тегов профиля. Пустая разница означает, что блок «Специализации»
/// и прайс согласованы и записывать нечего.
/// </remarks>
/// <param name="Add">Специализации активных услуг, которых нет в профиле.</param>
/// <param name="Remove">Теги профиля, у которых не осталось активных услуг.</param>
public sealed record BarberSpecializationChanges(
    IReadOnlyList<Guid> Add,
    IReadOnlyList<Guid> Remove)
{
    /// <summary>
    /// Записывать нечего: профиль уже совпадает с услугами.
    /// </summary>
    public bool IsEmpty => Add.Count == 0 && Remove.Count == 0;
}

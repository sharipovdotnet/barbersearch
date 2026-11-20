using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;

namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Специализация мастера в карточке кабинета.
/// </summary>
/// <remarks>
/// <see cref="Id"/> — запись связи, <see cref="SpecializationId"/> — строка справочника:
/// удаление адресуется второй, а показ и сравнение со справочником — тоже ей.
/// </remarks>
public sealed class BarberSpecializationItem
{
    /// <summary>
    /// Создаёт строку из специализации карточки.
    /// </summary>
    /// <param name="specialization">Специализация из собственного ответа.</param>
    public BarberSpecializationItem(BarberProfileSpecializationResponse specialization)
    {
        ArgumentNullException.ThrowIfNull(specialization);

        Id = specialization.Id;
        SpecializationId = specialization.SpecializationId;
        Name = specialization.Name;
        Description = specialization.Description;
    }

    /// <summary>
    /// Идентификатор записи «специализация мастера».
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Идентификатор специализации справочника.
    /// </summary>
    public Guid SpecializationId { get; }

    /// <summary>
    /// Название специализации.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Описание; <c>null</c>, если его нет.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// Описание заполнено.
    /// </summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
}

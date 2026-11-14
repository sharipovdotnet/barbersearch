using System.Collections.ObjectModel;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Группа строк списка по месяцу: заголовок «Ноябрь 2026» и сами записи.
/// </summary>
/// <remarks>
/// Наследуется от <see cref="ObservableCollection{T}"/>: <c>CollectionView</c> с группировкой
/// ждёт от группы именно коллекцию, а не пару «заголовок + список». Названия месяцев зависят
/// от пояса салона, поэтому ключ (<c>yyyy-MM</c>) и заголовок считает строка, а не сама группа.
/// </remarks>
public sealed class BookingMonthGroup : ObservableCollection<BookingListItem>
{
    /// <summary>
    /// Создаёт группу месяца.
    /// </summary>
    /// <param name="key">Ключ месяца в поясе салона: <c>yyyy-MM</c>.</param>
    /// <param name="title">Заголовок для показа: «Ноябрь 2026».</param>
    public BookingMonthGroup(string key, string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(title);

        Key = key;
        Title = title;
    }

    /// <summary>
    /// Ключ месяца: по нему догрузка находит существующую группу и не создаёт вторую.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Заголовок группы для показа.
    /// </summary>
    public string Title { get; }
}

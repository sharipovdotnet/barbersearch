using BarberSearch.Mobile.Core.Localization;

namespace BarberSearch.Mobile.Localization;

/// <summary>
/// Разметка XAML: <c>{loc:T Key='Текст'}</c> возвращает перевод исходной русской строки.
/// </summary>
/// <remarks>
/// <para>
/// Ключ — русский текст, как в вебе. Смена языка пересобирает Shell целиком, поэтому
/// расширению не нужна привязка: оно отдаёт строку в момент разбора XAML.
/// </para>
/// <para>
/// Текст с параметрами в XAML не используется: счётчики и подстановки живут в ViewModel,
/// а разметка берёт только статические подписи.
/// </para>
/// </remarks>
[ContentProperty(nameof(Key))]
public sealed class TranslateExtension : IMarkupExtension
{
    /// <summary>
    /// Русская строка — ключ каталога.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <inheritdoc />
    public object ProvideValue(IServiceProvider serviceProvider) =>
        string.IsNullOrEmpty(Key) ? string.Empty : Loc.T(Key);
}

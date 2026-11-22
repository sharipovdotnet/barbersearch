using BarberSearch.Domain.Salons;
using BarberSearch.Mobile.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Вариант «Что это за место?» в форме создания салона: тип, подпись и подсказка.
/// </summary>
/// <remarks>
/// <para>
/// Повторяет веб (<c>frontend/src/lib/place-kind.ts</c>): те же пять типов в том же порядке,
/// те же подписи «Барбершоп», «Студия», «Кабинет», «На дому», «Выезд» и те же короткие
/// подсказки. Подписи берутся через <see cref="Loc.T(string)"/> при создании варианта и не
/// кэшируются в статических полях: при смене языка приложение пересобирает Shell и создаёт
/// ViewModel заново, поэтому замороженная строка пережила бы переключение.
/// </para>
/// <para>
/// Тип задаётся только при создании — в домене нет смены вида места, — поэтому набор
/// вариантов строится один раз вместе с формой.
/// </para>
/// </remarks>
public sealed partial class BarberSalonKindOption : ObservableObject
{
    /// <summary>
    /// Создаёт вариант типа места с локализованными подписью и подсказкой.
    /// </summary>
    /// <param name="kind">Вид места приёма.</param>
    public BarberSalonKindOption(PlaceKind kind)
    {
        Kind = kind;
        Label = LabelFor(kind);
        Hint = HintFor(kind);
    }

    /// <summary>
    /// Вид места приёма, который уходит в API именем перечисления.
    /// </summary>
    public PlaceKind Kind { get; }

    /// <summary>
    /// Подпись варианта: «Барбершоп», «Студия», «Кабинет», «На дому», «Выезд».
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Подсказка под подписью: одна строка, объясняющая, чем тип отличается.
    /// </summary>
    public string Hint { get; }

    /// <summary>
    /// Выбран ли этот вариант в радио-группе «Что это за место?».
    /// </summary>
    /// <remarks>
    /// Пять вариантов рисуются <c>BindableLayout</c>, поэтому подсветка выбранной карточки
    /// привязана к самому варианту, а не к отдельному индексу в форме. Флаг ведёт
    /// <see cref="BarberSalonCreateViewModel.SelectedKindOption"/>: в группе
    /// он всегда ровно у одного варианта.
    /// </remarks>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Типы места в порядке радио-группы: от общего барбершопа к частному выезду.
    /// </summary>
    /// <returns>
    /// Пять вариантов; массив создаётся на каждый вызов, чтобы подписи соответствовали
    /// текущему языку.
    /// </returns>
    public static IReadOnlyList<BarberSalonKindOption> CreateAll() =>
    [
        new(PlaceKind.Salon),
        new(PlaceKind.RentalStudio),
        new(PlaceKind.SoloStudio),
        new(PlaceKind.Home),
        new(PlaceKind.Mobile),
    ];

    /// <summary>
    /// Значение «Показывать в каталоге» по умолчанию для нового места.
    /// </summary>
    /// <remarks>
    /// Барбершоп и студия аренды кресел без каталога не имеют смысла — домен это запрещает;
    /// кабинет по умолчанию скрыт, потому что кабинет чаще личный; дом и выезд скрыты всегда.
    /// Значение повторяет веб (<c>defaultIsListed</c>).
    /// </remarks>
    /// <param name="kind">Вид места приёма.</param>
    /// <returns><c>true</c> только для барбершопа и студии аренды кресел.</returns>
    public static bool DefaultIsListed(PlaceKind kind) =>
        kind is PlaceKind.Salon or PlaceKind.RentalStudio;

    /// <summary>
    /// Может ли место вообще быть показано в каталоге.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Дом и выезд скрыты всегда: адрес на дому и зона выезда не публикуются в каталоге, а
    /// скрытое место не проходит модерацию. Кабинет бывает и публичным, и скрытым — это
    /// решает барбер флагом <c>isListed</c>, поэтому переключатель доступен только для него.
    /// </para>
    /// <para>
    /// Повторяет веб (<c>isListedAllowed</c>) и доменное правило
    /// <c>salon.isListed.kind</c>: домен запрещает показывать дом и выезд и прятать
    /// барбершоп со студией.
    /// </para>
    /// </remarks>
    /// <param name="kind">Вид места приёма.</param>
    /// <returns><c>false</c> для <see cref="PlaceKind.Home"/> и <see cref="PlaceKind.Mobile"/>.</returns>
    public static bool IsListedAllowed(PlaceKind kind) =>
        kind is not (PlaceKind.Home or PlaceKind.Mobile);

    /// <summary>
    /// Подпись типа места — исходная русская строка каталога.
    /// </summary>
    private static string LabelFor(PlaceKind kind) => Loc.T(kind switch
    {
        PlaceKind.Salon => "Барбершоп",
        PlaceKind.RentalStudio => "Студия",
        PlaceKind.SoloStudio => "Кабинет",
        PlaceKind.Home => "На дому",
        PlaceKind.Mobile => "Выезд",
        _ => kind.ToString(),
    });

    /// <summary>
    /// Подсказка типа места — исходная русская строка каталога.
    /// </summary>
    private static string HintFor(PlaceKind kind) => Loc.T(kind switch
    {
        PlaceKind.Salon => "обычный салон со штатом",
        PlaceKind.RentalStudio => "сдаёт кресла другим барберам",
        PlaceKind.SoloStudio => "принимаете только вы",
        PlaceKind.Home => "адрес виден в карточке",
        PlaceKind.Mobile => "приезжаете к клиенту",
        _ => string.Empty,
    });
}

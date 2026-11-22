using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using BarberSearch.Contracts.Addresses.SuggestAddresses;
using BarberSearch.Contracts.Addresses.SuggestCities;
using BarberSearch.Contracts.Salons.CreateSalon;
using BarberSearch.Domain.Salons;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Salons;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Форма «Создать салон» в разделе «Салоны» кабинета мастера.
/// </summary>
/// <remarks>
/// <para>
/// Повторяет веб (<c>frontend/src/components/salon/salon-form.tsx</c>,
/// <c>frontend/src/lib/salon-form.ts</c>) в режиме владельца: те же поля, те же проверки,
/// то же тело <c>POST api/salons/own</c> и тот же признак «первый салон станет основным»,
/// который сервер выставляет сам. Отличий три, и все они мобильные: почтового индекса нет
/// вовсе (веб скрывает его владельцу при создании), часовой пояс выбирается из
/// курированного списка (<see cref="SalonTimeZones"/>), а дробная часть координат
/// принимается и с запятой — на русской клавиатуре телефона десятичный разделитель запятая.
/// </para>
/// <para>
/// Адрес вводится только после города: подсказки адреса ищут внутри города, а без него
/// поле остаётся выключенным с подсказкой «Сначала выберите город.». Город и адрес
/// подсказываются с паузой, и подсказки — улучшение ввода: их отказ ничего не блокирует.
/// </para>
/// <para>
/// Видимость в каталоге определяется типом места: барбершоп и студия обязаны быть в
/// каталоге, дом и выезд всегда скрыты, и только кабинет даёт переключатель. Смена типа
/// сбрасывает флаг к значению по умолчанию, как в вебе. Домен дополнительно проверяет
/// совместимость (<c>salon.isListed.kind</c>), поэтому при отправке флаг нормализуется по
/// типу, а не уходит как есть.
/// </para>
/// <para>
/// Отправка — запись: она не повторяется сама. После успеха форма объявляет результат
/// событием <see cref="Saved"/>, и страница закрывается; после отказа поля заполняются
/// серверными кодами, а общий текст показывается только тогда, когда ни одно поле не
/// названо.
/// </para>
/// </remarks>
public sealed partial class BarberSalonCreateViewModel : ObservableObject
{
    /// <summary>
    /// Предел длины телефона: то же значение объявлено сервером для салона.
    /// </summary>
    public const int MaxPhoneLength = 32;

    /// <summary>
    /// Предел длины адреса сайта: домен не принимает длиннее.
    /// </summary>
    public const int MaxWebsiteLength = 2048;

    /// <summary>
    /// Минимальная широта.
    /// </summary>
    public const double MinLatitude = -90;

    /// <summary>
    /// Максимальная широта.
    /// </summary>
    public const double MaxLatitude = 90;

    /// <summary>
    /// Минимальная долгота.
    /// </summary>
    public const double MinLongitude = -180;

    /// <summary>
    /// Максимальная долгота.
    /// </summary>
    public const double MaxLongitude = 180;

    private readonly ISalonsApi _salonsApi;

    /// <summary>
    /// Провайдерский токен выбранного города: уточняет подсказки адреса и никогда не
    /// попадает в тело создания салона.
    /// </summary>
    private string? _cityProviderId;

    /// <summary>
    /// Что именно заполнила выбранная подсказка адреса: смена города снимает только те
    /// поля, которые всё ещё равны снимку, а ручная правка адреса снимает координаты.
    /// </summary>
    private AddressSuggestionSnapshot? _addressSnapshot;

    private string? _nameError;
    private string? _addressError;
    private string? _cityError;
    private string? _countryError;
    private string? _timeZoneError;
    private string? _latitudeError;
    private string? _longitudeError;
    private string? _phoneNumberError;
    private string? _websiteError;
    private string? _isListedError;

    /// <summary>
    /// Создаёт форму создания салона.
    /// </summary>
    /// <remarks>
    /// Экран берёт свежий экземпляр на каждый визит, поэтому метода <c>Initialize</c> нет:
    /// начальное состояние — это состояние конструктора.
    /// </remarks>
    /// <param name="salonsApi">Создание собственного салона.</param>
    /// <param name="citySuggestions">Подсказки города (общий класс с фильтром «Поиск»).</param>
    /// <param name="addressSuggestions">Подсказки адреса внутри выбранного города.</param>
    public BarberSalonCreateViewModel(
        ISalonsApi salonsApi,
        CitySuggestions citySuggestions,
        AddressSuggestions addressSuggestions)
    {
        ArgumentNullException.ThrowIfNull(salonsApi);
        ArgumentNullException.ThrowIfNull(citySuggestions);
        ArgumentNullException.ThrowIfNull(addressSuggestions);

        _salonsApi = salonsApi;
        CitySuggestions = citySuggestions;
        AddressSuggestions = addressSuggestions;

        KindOptions = BarberSalonKindOption.CreateAll();
        _selectedKindOption = KindOptions[0];
        _isListed = BarberSalonKindOption.DefaultIsListed(_selectedKindOption.Kind);

        TimeZones = SalonTimeZones.Build(TimeZoneInfo.Local.Id);
        _timeZoneId = TimeZones[0];
    }

    /// <summary>
    /// Пять типов места в порядке радио-группы.
    /// </summary>
    public IReadOnlyList<BarberSalonKindOption> KindOptions { get; }

    /// <summary>
    /// Выбранный тип места; для пикера — двусторонняя привязка.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KindHint))]
    [NotifyPropertyChangedFor(nameof(CanToggleListing))]
    [NotifyPropertyChangedFor(nameof(SubmitText))]
    private BarberSalonKindOption _selectedKindOption;

    /// <summary>
    /// Подсказка выбранного типа места: «обычный салон со штатом» и соседние.
    /// </summary>
    public string KindHint => SelectedKindOption.Hint;

    /// <summary>
    /// Переключатель «Показывать в каталоге» доступен: только кабинет.
    /// </summary>
    /// <remarks>
    /// У барбершопа и студии флаг всегда <c>true</c>, у дома и выезда — всегда <c>false</c>:
    /// это правило домена, а не предпочтение мастера.
    /// </remarks>
    public bool CanToggleListing => SelectedKindOption.Kind == PlaceKind.SoloStudio;

    /// <summary>
    /// Подсказка под переключателем каталога — текст веба.
    /// </summary>
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Свойство связано XAML: статическое свойство сломало бы привязку.")]
    public string ListingHint => BarberSalonCreateText.ListingHint;

    /// <summary>
    /// Показывать ли место в каталоге.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubmitText))]
    private bool _isListed;

    /// <summary>
    /// Ошибка видимости из ответа сервера (<c>salon.isListed.kind</c>).
    /// </summary>
    /// <remarks>
    /// Клиентских комбинаций здесь нет: тип места выставляет флаг по умолчанию и не даёт
    /// включить несовместимую пару, поэтому текст приходит только кодом домена.
    /// </remarks>
    public string? IsListedError => _isListedError;

    /// <summary>
    /// Название салона.
    /// </summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>
    /// Ошибка названия.
    /// </summary>
    public string? NameError => _nameError;

    /// <summary>
    /// Город.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEnterAddress))]
    [NotifyPropertyChangedFor(nameof(AddressHint))]
    private string _city = string.Empty;

    /// <summary>
    /// Ошибка города.
    /// </summary>
    public string? CityError => _cityError;

    /// <summary>
    /// Поле адреса доступно: город уже выбран.
    /// </summary>
    public bool CanEnterAddress => !string.IsNullOrWhiteSpace(City);

    /// <summary>
    /// Подсказка под полем адреса, пока город пуст.
    /// </summary>
    public string AddressHint => CanEnterAddress
        ? string.Empty
        : BarberSalonCreateText.ChooseCityFirst;

    /// <summary>
    /// Адрес: улица и дом без города.
    /// </summary>
    [ObservableProperty]
    private string _address = string.Empty;

    /// <summary>
    /// Ошибка адреса.
    /// </summary>
    public string? AddressError => _addressError;

    /// <summary>
    /// Страна.
    /// </summary>
    [ObservableProperty]
    private string _country = string.Empty;

    /// <summary>
    /// Ошибка страны.
    /// </summary>
    public string? CountryError => _countryError;

    /// <summary>
    /// Часовые пояса для пикера: пояс устройства первым.
    /// </summary>
    public IReadOnlyList<string> TimeZones { get; }

    /// <summary>
    /// Часовой пояс салона (IANA).
    /// </summary>
    [ObservableProperty]
    private string _timeZoneId = string.Empty;

    /// <summary>
    /// Ошибка часового пояса.
    /// </summary>
    public string? TimeZoneError => _timeZoneError;

    /// <summary>
    /// Широта строкой: поле ввода не хранит число.
    /// </summary>
    [ObservableProperty]
    private string _latitude = string.Empty;

    /// <summary>
    /// Ошибка широты.
    /// </summary>
    public string? LatitudeError => _latitudeError;

    /// <summary>
    /// Долгота строкой: поле ввода не хранит число.
    /// </summary>
    [ObservableProperty]
    private string _longitude = string.Empty;

    /// <summary>
    /// Ошибка долготы.
    /// </summary>
    public string? LongitudeError => _longitudeError;

    /// <summary>
    /// Телефон салона; пусто — поля нет.
    /// </summary>
    [ObservableProperty]
    private string _phoneNumber = string.Empty;

    /// <summary>
    /// Ошибка телефона.
    /// </summary>
    public string? PhoneNumberError => _phoneNumberError;

    /// <summary>
    /// Адрес сайта салона; пусто — поля нет.
    /// </summary>
    [ObservableProperty]
    private string _websiteUrl = string.Empty;

    /// <summary>
    /// Ошибка сайта.
    /// </summary>
    public string? WebsiteError => _websiteError;

    /// <summary>
    /// Общая ошибка формы: отказ сервера, который не назвал ни одного поля.
    /// </summary>
    [ObservableProperty]
    private string? _formError;

    /// <summary>
    /// Идёт отправка: кнопка не нажимается дважды.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubmitText))]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    private bool _isSaving;

    /// <summary>
    /// Подпись кнопки отправки: зависит от видимости в каталоге и от текущей отправки.
    /// </summary>
    public string SubmitText => IsSaving
        ? IsListed ? Loc.T("Отправляем…") : Loc.T("Создаём…")
        : IsListed ? Loc.T("Отправить на проверку") : Loc.T("Создать место");

    /// <summary>
    /// Кнопка отправки доступна: отправка ещё не идёт.
    /// </summary>
    public bool CanSubmit => !IsSaving;

    /// <summary>
    /// Подсказки города (тот же класс, что у фильтра «Поиск»).
    /// </summary>
    public CitySuggestions CitySuggestions { get; }

    /// <summary>
    /// Подсказки адреса внутри выбранного города.
    /// </summary>
    public AddressSuggestions AddressSuggestions { get; }

    /// <summary>
    /// Салон создан: страница объявляет сообщение и закрывается.
    /// </summary>
    public event EventHandler<string>? Saved;

    /// <summary>
    /// Завершение последнего запроса подсказок города — тестам, чтобы не ждать паузу.
    /// </summary>
    internal Task CityQueryCompletion { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Завершение последнего запроса подсказок адреса — тестам, чтобы не ждать паузу.
    /// </summary>
    internal Task AddressQueryCompletion { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Выбирает город из подсказки: подставляет город и страну и запоминает провайдера.
    /// </summary>
    /// <remarks>
    /// Подсказанный адрес снимается по снимку: если он всё ещё совпадает с прежней
    /// подсказкой, он относился к другому городу. Страна из пустой подсказки введённую
    /// руками не затирает — то же правило у веба.
    /// </remarks>
    /// <param name="suggestion">Выбранная подсказка города.</param>
    [RelayCommand]
    private void UseCitySuggestion(CitySuggestionResponse suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);

        // Выбор запоминается до подстановки: повторный запрос для того же города не нужен
        // и снова открыл бы список сразу после выбора.
        CitySuggestions.Select(suggestion);

        ClearSuggestedAddressFields();

        City = suggestion.City;

        if (!string.IsNullOrWhiteSpace(suggestion.Country))
        {
            Country = suggestion.Country;
        }

        // OnCityChanged уже обнулил провайдера: токен ставится после подстановки города.
        _cityProviderId = suggestion.ProviderId;

        AddressSuggestions.Close();
    }

    /// <summary>
    /// Выбирает адрес из подсказки: подставляет улицу и координаты.
    /// </summary>
    /// <remarks>
    /// Почтового индекса в мобильной форме нет — он не уходит и не заполняется. Координаты
    /// запоминаются снимком: ручная правка улицы снимет их, потому что они относились к
    /// выбранному дому.
    /// </remarks>
    /// <param name="suggestion">Выбранная подсказка адреса.</param>
    [RelayCommand]
    private void UseAddressSuggestion(AddressSuggestionResponse suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);

        AddressSuggestions.Select(suggestion);

        // Пока человек выбирает адрес, подсказки города не нужны: открытый городской список
        // над полем адреса остался бы висеть поверх формы.
        CitySuggestions.Close();

        // Старый снимок снимается до подстановки: новые значения не должны «отцепиться»
        // логикой ручной правки, которая срабатывает на изменение поля адреса.
        _addressSnapshot = null;

        var address = AddressSuggestions.SuggestedValue(suggestion);

        Address = address;

        var latitude = FormatCoordinate(suggestion.Latitude);
        var longitude = FormatCoordinate(suggestion.Longitude);

        if (latitude is not null)
        {
            Latitude = latitude;
        }

        if (longitude is not null)
        {
            Longitude = longitude;
        }

        _addressSnapshot = new AddressSuggestionSnapshot(address, latitude, longitude);

        SetError(ref _addressError, nameof(AddressError));
        FormError = null;
    }

    /// <summary>
    /// Создаёт салон и объявляет результат.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Проверки повторяют веб: обязательные название, адрес, город, страна и часовой пояс,
    /// числовые координаты в диапазоне, необязательный сайт формата <c>http(s)</c> не длиннее
    /// 2048 символов и необязательный телефон не длиннее 32 символов.
    /// </para>
    /// <para>
    /// Запись не повторяется сама: после сбоя неизвестно, создан ли салон, и человек сам
    /// решает, отправить ли ещё раз. Отмена экраном ошибку не показывает.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task SubmitAsync(CancellationToken cancellationToken)
    {
        if (IsSaving)
        {
            return;
        }

        if (!Validate(out var latitude, out var longitude))
        {
            return;
        }

        var kind = SelectedKindOption.Kind;
        var isListed = NormalizeIsListed(kind, IsListed);

        var request = new CreateSalonRequest(
            Name.Trim(),
            Address.Trim(),
            City.Trim(),
            Country.Trim(),
            TimeZoneId.Trim(),
            latitude,
            longitude,
            PostalCode: null,
            PhoneNumber: Optional(PhoneNumber),
            WebsiteUrl: Optional(WebsiteUrl),
            kind,
            isListed);

        IsSaving = true;
        FormError = null;

        try
        {
            await _salonsApi.CreateOwnAsync(request, cancellationToken).ConfigureAwait(true);

            // Флаг снимается до события: страница в обработчике уже закрывает форму.
            IsSaving = false;

            Saved?.Invoke(this, BarberSalonCreateText.Created(isListed));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Экран ушёл: ошибку показывать некому.
        }
        catch (BarberSearchApiException exception)
        {
            ApplyServerErrors(exception);
        }
        catch (Exception exception)
        {
            FormError = BarberSalonCreateText.SaveError(exception);
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>
    /// Тип места изменился: флаг каталога возвращается к значению по умолчанию.
    /// </summary>
    /// <remarks>
    /// Остальные поля не трогаются: барбер уже мог заполнить адрес и контакты. Ошибка
    /// видимости снимается вместе с прежним выбором.
    /// </remarks>
    /// <param name="value">Новый тип места.</param>
    partial void OnSelectedKindOptionChanged(BarberSalonKindOption value)
    {
        IsListed = BarberSalonKindOption.DefaultIsListed(value.Kind);
        SetError(ref _isListedError, nameof(IsListedError));
        FormError = null;

        OnPropertyChanged(nameof(KindHint));
        OnPropertyChanged(nameof(CanToggleListing));
        OnPropertyChanged(nameof(SubmitText));
        OnPropertyChanged(nameof(IsListed));
    }

    /// <summary>
    /// Название изменилось: ошибка поля снимается.
    /// </summary>
    /// <param name="value">Новое название.</param>
    partial void OnNameChanged(string value)
    {
        SetError(ref _nameError, nameof(NameError));
        FormError = null;
    }

    /// <summary>
    /// Город изменился вручную: провайдер и подсказанный адрес больше не действительны.
    /// </summary>
    /// <remarks>
    /// Снимаются только поля, которые всё ещё равны подсказке: введённое руками остаётся.
    /// Список подсказок адреса закрывается, а подсказки города запрашиваются с паузой.
    /// </remarks>
    /// <param name="value">Новый город.</param>
    partial void OnCityChanged(string value)
    {
        _cityProviderId = null;

        ClearSuggestedAddressFields();
        AddressSuggestions.Close();

        SetError(ref _cityError, nameof(CityError));
        FormError = null;

        CityQueryCompletion = CitySuggestions.QueryAsync(value);
    }

    /// <summary>
    /// Адрес изменился вручную: координаты выбранного дома к нему больше не относятся.
    /// </summary>
    /// <remarks>
    /// Подсказки адреса запрашиваются для нового текста и того города, который выбран
    /// сейчас: провайдерский токен города уточняет поиск, когда он есть. Список городов
    /// при этом закрывается: два открытых списка под полями читались бы как одна подсказка.
    /// </remarks>
    /// <param name="value">Новый адрес.</param>
    partial void OnAddressChanged(string value)
    {
        DetachSuggestedCoordinates();

        CitySuggestions.Close();

        SetError(ref _addressError, nameof(AddressError));
        FormError = null;

        AddressQueryCompletion = AddressSuggestions.QueryAsync(value, City, _cityProviderId);
    }

    /// <summary>
    /// Страна изменилась: ошибка поля снимается.
    /// </summary>
    /// <param name="value">Новая страна.</param>
    partial void OnCountryChanged(string value)
    {
        SetError(ref _countryError, nameof(CountryError));
        FormError = null;
    }

    /// <summary>
    /// Часовой пояс изменился: ошибка поля снимается.
    /// </summary>
    /// <param name="value">Новый пояс.</param>
    partial void OnTimeZoneIdChanged(string value)
    {
        SetError(ref _timeZoneError, nameof(TimeZoneError));
        FormError = null;
    }

    /// <summary>
    /// Широта изменилась: ошибка поля снимается.
    /// </summary>
    /// <param name="value">Новая широта строкой.</param>
    partial void OnLatitudeChanged(string value)
    {
        SetError(ref _latitudeError, nameof(LatitudeError));
        FormError = null;
    }

    /// <summary>
    /// Долгота изменилась: ошибка поля снимается.
    /// </summary>
    /// <param name="value">Новая долгота строкой.</param>
    partial void OnLongitudeChanged(string value)
    {
        SetError(ref _longitudeError, nameof(LongitudeError));
        FormError = null;
    }

    /// <summary>
    /// Телефон изменился: ошибка поля снимается.
    /// </summary>
    /// <param name="value">Новый телефон.</param>
    partial void OnPhoneNumberChanged(string value)
    {
        SetError(ref _phoneNumberError, nameof(PhoneNumberError));
        FormError = null;
    }

    /// <summary>
    /// Сайт изменился: ошибка поля снимается.
    /// </summary>
    /// <param name="value">Новый адрес сайта.</param>
    partial void OnWebsiteUrlChanged(string value)
    {
        SetError(ref _websiteError, nameof(WebsiteError));
        FormError = null;
    }

    /// <summary>
    /// Проверяет поля формы перед отправкой.
    /// </summary>
    /// <param name="latitude">Разобранная широта; <c>0</c> при ошибке.</param>
    /// <param name="longitude">Разобранная долгота; <c>0</c> при ошибке.</param>
    /// <returns><c>true</c>, если можно отправлять.</returns>
    private bool Validate(out double latitude, out double longitude)
    {
        ClearFieldErrors();

        latitude = 0;
        longitude = 0;

        var isValid = true;

        if (string.IsNullOrWhiteSpace(Name))
        {
            SetError(ref _nameError, nameof(NameError), BarberSalonCreateText.NameRequired);
            isValid = false;
        }

        if (string.IsNullOrWhiteSpace(Address))
        {
            SetError(
                ref _addressError,
                nameof(AddressError),
                BarberSalonCreateText.AddressRequired);
            isValid = false;
        }

        if (string.IsNullOrWhiteSpace(City))
        {
            SetError(ref _cityError, nameof(CityError), BarberSalonCreateText.CityRequired);
            isValid = false;
        }

        if (string.IsNullOrWhiteSpace(Country))
        {
            SetError(
                ref _countryError,
                nameof(CountryError),
                BarberSalonCreateText.CountryRequired);
            isValid = false;
        }

        if (string.IsNullOrWhiteSpace(TimeZoneId))
        {
            SetError(
                ref _timeZoneError,
                nameof(TimeZoneError),
                BarberSalonCreateText.TimeZoneRequired);
            isValid = false;
        }

        if (!TryParseCoordinate(Latitude, MinLatitude, MaxLatitude, out latitude))
        {
            SetError(
                ref _latitudeError,
                nameof(LatitudeError),
                BarberSalonCreateText.LatitudeRange);
            isValid = false;
        }

        if (!TryParseCoordinate(Longitude, MinLongitude, MaxLongitude, out longitude))
        {
            SetError(
                ref _longitudeError,
                nameof(LongitudeError),
                BarberSalonCreateText.LongitudeRange);
            isValid = false;
        }

        if (!string.IsNullOrWhiteSpace(WebsiteUrl)
            && !IsValidWebsite(WebsiteUrl.Trim()))
        {
            SetError(
                ref _websiteError,
                nameof(WebsiteError),
                BarberSalonCreateText.WebsiteInvalid);
            isValid = false;
        }

        if (PhoneNumber.Trim().Length > MaxPhoneLength)
        {
            SetError(
                ref _phoneNumberError,
                nameof(PhoneNumberError),
                BarberSalonCreateText.PhoneTooLong);
            isValid = false;
        }

        return isValid;
    }

    /// <summary>
    /// Разбирает координату с точкой или запятой и проверяет диапазон.
    /// </summary>
    /// <remarks>
    /// Запятая — мобильная адаптация: на русской клавиатуре телефона десятичный разделитель
    /// запятая, и «56,8389» должно приниматься как «56.8389». Инвариантная культура нужна,
    /// чтобы поведение не зависело от языка интерфейса.
    /// </remarks>
    /// <param name="text">Текст поля.</param>
    /// <param name="min">Минимум диапазона.</param>
    /// <param name="max">Максимум диапазона.</param>
    /// <param name="value">Разобранное значение.</param>
    /// <returns><c>true</c>, если это конечное число в диапазоне.</returns>
    private static bool TryParseCoordinate(string text, double min, double max, out double value)
    {
        value = 0;

        var trimmed = text.Trim();

        if (trimmed.Length == 0)
        {
            return false;
        }

        var normalized = trimmed.Replace(',', '.');

        return double.TryParse(
                normalized,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value)
            && double.IsFinite(value)
            && value >= min
            && value <= max;
    }

    /// <summary>
    /// Проверяет необязательный адрес сайта: <c>http(s)</c> и не длиннее 2048 символов.
    /// </summary>
    /// <param name="value">Очищенный адрес сайта.</param>
    /// <returns><c>true</c>, если адрес допустим.</returns>
    private static bool IsValidWebsite(string value)
    {
        if (value.Length > MaxWebsiteLength)
        {
            return false;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal));
    }

    /// <summary>
    /// Приводит флаг каталога к правилу типа места.
    /// </summary>
    /// <remarks>
    /// Домен запрещает показывать дом и выезд и прятать барбершоп со студией, поэтому
    /// несовместимая пара нормализуется до отправки, а не уходит на сервер.
    /// </remarks>
    /// <param name="kind">Тип места.</param>
    /// <param name="isListed">Флаг из формы.</param>
    /// <returns>Флаг, который примет домен.</returns>
    private static bool NormalizeIsListed(PlaceKind kind, bool isListed) => kind switch
    {
        PlaceKind.Home or PlaceKind.Mobile => false,
        PlaceKind.Salon or PlaceKind.RentalStudio => true,
        _ => isListed,
    };

    /// <summary>
    /// Пустая строка не отправляется: сервер понимает <c>null</c> как «поля нет».
    /// </summary>
    /// <param name="value">Значение поля.</param>
    /// <returns>Очищенное значение либо <c>null</c>.</returns>
    private static string? Optional(string value)
    {
        var trimmed = value.Trim();

        return trimmed.Length == 0 ? null : trimmed;
    }

    /// <summary>
    /// Координата в виде строки формы: инвариантная культура, <c>null</c> не затирает поле.
    /// </summary>
    /// <param name="value">Координата из подсказки.</param>
    /// <returns>Строка координаты либо <c>null</c>.</returns>
    private static string? FormatCoordinate(double? value) => value is { } coordinate
        ? coordinate.ToString(CultureInfo.InvariantCulture)
        : null;

    /// <summary>
    /// Снимает поля, которые всё ещё равны выбранной подсказке адреса.
    /// </summary>
    /// <remarks>
    /// Введённое руками остаётся: снимок помнит значения подсказки, а не происхождение поля.
    /// Снимок обнуляется до правки, чтобы изменение поля адреса не отцепило координаты
    /// повторно.
    /// </remarks>
    private void ClearSuggestedAddressFields()
    {
        var snapshot = _addressSnapshot;

        _addressSnapshot = null;

        if (snapshot is null)
        {
            return;
        }

        if (string.Equals(Address, snapshot.Address, StringComparison.Ordinal))
        {
            Address = string.Empty;
        }

        if (snapshot.Latitude is { } latitude
            && string.Equals(Latitude, latitude, StringComparison.Ordinal))
        {
            Latitude = string.Empty;
        }

        if (snapshot.Longitude is { } longitude
            && string.Equals(Longitude, longitude, StringComparison.Ordinal))
        {
            Longitude = string.Empty;
        }
    }

    /// <summary>
    /// Ручная правка адреса снимает координаты выбранного дома.
    /// </summary>
    /// <remarks>
    /// Координаты, введённые руками, не трогаются: их нет в снимке. Снимок после правки
    /// обнуляется — поле больше не подсказанное.
    /// </remarks>
    private void DetachSuggestedCoordinates()
    {
        var snapshot = _addressSnapshot;

        _addressSnapshot = null;

        if (snapshot is null)
        {
            return;
        }

        if (snapshot.Latitude is { } latitude
            && string.Equals(Latitude, latitude, StringComparison.Ordinal))
        {
            Latitude = string.Empty;
        }

        if (snapshot.Longitude is { } longitude
            && string.Equals(Longitude, longitude, StringComparison.Ordinal))
        {
            Longitude = string.Empty;
        }
    }

    /// <summary>
    /// Раскладывает отказ сервера по полям, оставляя общий текст только без совпадений.
    /// </summary>
    /// <param name="exception">Отказ API.</param>
    private void ApplyServerErrors(BarberSearchApiException exception)
    {
        var errors = BarberSalonCreateText.FieldErrors(exception);
        var matched = false;

        if (errors.Name is { } name)
        {
            SetError(ref _nameError, nameof(NameError), name);
            matched = true;
        }

        if (errors.Address is { } address)
        {
            SetError(ref _addressError, nameof(AddressError), address);
            matched = true;
        }

        if (errors.City is { } city)
        {
            SetError(ref _cityError, nameof(CityError), city);
            matched = true;
        }

        if (errors.Country is { } country)
        {
            SetError(ref _countryError, nameof(CountryError), country);
            matched = true;
        }

        if (errors.TimeZoneId is { } timeZoneId)
        {
            SetError(ref _timeZoneError, nameof(TimeZoneError), timeZoneId);
            matched = true;
        }

        if (errors.WebsiteUrl is { } websiteUrl)
        {
            SetError(ref _websiteError, nameof(WebsiteError), websiteUrl);
            matched = true;
        }

        if (errors.Latitude is { } latitude)
        {
            SetError(ref _latitudeError, nameof(LatitudeError), latitude);
            matched = true;
        }

        if (errors.Longitude is { } longitude)
        {
            SetError(ref _longitudeError, nameof(LongitudeError), longitude);
            matched = true;
        }

        if (errors.IsListed is { } isListed)
        {
            SetError(ref _isListedError, nameof(IsListedError), isListed);
            matched = true;
        }

        if (!matched)
        {
            FormError = BarberSalonCreateText.SaveError(exception);
        }
    }

    /// <summary>
    /// Сбрасывает ошибки всех полей перед новой проверкой.
    /// </summary>
    private void ClearFieldErrors()
    {
        SetError(ref _nameError, nameof(NameError));
        SetError(ref _addressError, nameof(AddressError));
        SetError(ref _cityError, nameof(CityError));
        SetError(ref _countryError, nameof(CountryError));
        SetError(ref _timeZoneError, nameof(TimeZoneError));
        SetError(ref _latitudeError, nameof(LatitudeError));
        SetError(ref _longitudeError, nameof(LongitudeError));
        SetError(ref _phoneNumberError, nameof(PhoneNumberError));
        SetError(ref _websiteError, nameof(WebsiteError));
        SetError(ref _isListedError, nameof(IsListedError));
    }

    /// <summary>
    /// Меняет текст ошибки поля и уведомляет привязку только при реальном изменении.
    /// </summary>
    /// <param name="field">Поле ошибки.</param>
    /// <param name="propertyName">Имя публичного свойства ошибки.</param>
    /// <param name="value">Новый текст; <c>null</c> — ошибки нет.</param>
    private void SetError(ref string? field, string propertyName, string? value = null)
    {
        if (string.Equals(field, value, StringComparison.Ordinal))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    /// <summary>
    /// Что именно заполнила выбранная подсказка адреса.
    /// </summary>
    /// <param name="Address">Подставленный адрес.</param>
    /// <param name="Latitude">Подставленная широта; <c>null</c> — подсказка её не дала.</param>
    /// <param name="Longitude">Подставленная долгота; <c>null</c> — подсказка её не дала.</param>
    private sealed record AddressSuggestionSnapshot(
        string Address,
        string? Latitude,
        string? Longitude);
}

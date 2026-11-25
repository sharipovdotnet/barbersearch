using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using BarberSearch.Contracts.BarberProfiles.AddBarberProfileService;
using BarberSearch.Contracts.BarberProfiles.UpdateBarberProfileService;
using BarberSearch.Contracts.HairServices.GetHairServices;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Barber;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Редактор услуги в прайсе: добавление из справочника или правка цены и длительности.
/// </summary>
/// <remarks>
/// <para>
/// Справочник читается лениво — при открытии добавления, а не вместе с прайсом: он
/// большой, а нужен только в одном диалоге. Уже добавленные услуги из выдачи убираются
/// по <c>HairServiceId</c>: повторное добавление сервер отверг бы кодом дубликата.
/// Специализация выбранной услуги, которой у мастера ещё нет, добавляется автоматически:
/// отдельной кнопки «Добавить специализацию» в разделе «Профиль» больше нет.
/// </para>
/// <para>
/// Цена ограничена двумя знаками после запятой и суммой 99 999 999,99, длительность —
/// 1..1440 минутами. Это клиентская граница, а не источник истины: серверные коды
/// (<c>duplicate</c>, <c>updateService.notFound</c>, <c>updateDuration</c>) раскладываются
/// по тем же полям, чтобы человек видел причину, а не общий текст.
/// </para>
/// </remarks>
public sealed partial class BarberServiceEditorViewModel : ObservableObject
{
    private const decimal MaxPrice = 99_999_999.99m;
    private const int MinDurationMinutes = 1;
    private const int MaxDurationMinutes = 1440;

    private static string PriceRequiredText => Loc.T("Укажите цену.");
    private static string PriceRangeText => Loc.T("Цена — число от 0 до 99 999 999,99.");
    private static string PriceDecimalsText => Loc.T("Цена — не больше двух знаков после запятой.");
    private static string DurationRangeText => Loc.T("Длительность — от 1 до 1440 минут.");
    private static string CatalogFailedText => Loc.T("Не удалось загрузить каталог услуг.");
    private static string SaveFailedText => Loc.T("Не удалось сохранить услугу. Попробуйте ещё раз.");
    private static string ChooseServiceText => Loc.T("Выберите услугу из справочника.");

    private readonly IBarberProfileApi _profileApi;
    private readonly IBarberSearchApi _searchApi;
    private readonly HashSet<Guid> _existingHairServiceIds = [];
    private readonly HashSet<Guid> _attachedSpecializationIds = [];

    private Task _catalogTask = Task.CompletedTask;
    private BarberServiceItem? _editedService;

    /// <summary>
    /// Создаёт редактор услуги.
    /// </summary>
    /// <param name="profileApi">Добавление и правка услуги.</param>
    /// <param name="searchApi">Публичный справочник услуг.</param>
    public BarberServiceEditorViewModel(
        IBarberProfileApi profileApi,
        IBarberSearchApi searchApi)
    {
        ArgumentNullException.ThrowIfNull(profileApi);
        ArgumentNullException.ThrowIfNull(searchApi);

        _profileApi = profileApi;
        _searchApi = searchApi;
    }

    /// <summary>
    /// Профиль, в прайс которого пишет редактор; <c>null</c> до инициализации.
    /// </summary>
    public Guid? BarberProfileId { get; private set; }

    /// <summary>
    /// Режим добавления: услуга выбирается из справочника.
    /// </summary>
    [ObservableProperty]
    private bool _isAddMode;

    /// <summary>
    /// Режим правки: услуга фиксирована, меняются только цена и длительность.
    /// </summary>
    [ObservableProperty]
    private bool _isEditMode;

    /// <summary>
    /// Выбранная услуга справочника в режиме добавления.
    /// </summary>
    [ObservableProperty]
    private BarberServiceCatalogItem? _selectedCatalogService;

    /// <summary>
    /// Цена строкой: поле ввода не умеет хранить десятичное число.
    /// </summary>
    [ObservableProperty]
    private string _priceText = string.Empty;

    /// <summary>
    /// Длительность строкой, минуты.
    /// </summary>
    [ObservableProperty]
    private string _durationText = string.Empty;

    /// <summary>
    /// Название услуги в режиме правки: заголовок диалога.
    /// </summary>
    [ObservableProperty]
    private string _serviceName = string.Empty;

    /// <summary>
    /// Справочник услуг читается.
    /// </summary>
    [ObservableProperty]
    private bool _isLoadingCatalog;

    /// <summary>
    /// Причина сбоя справочника; живёт внутри диалога добавления.
    /// </summary>
    [ObservableProperty]
    private string? _catalogError;

    /// <summary>
    /// В справочнике не осталось услуг, которых нет в прайсе.
    /// </summary>
    [ObservableProperty]
    private bool _isCatalogEmpty;

    /// <summary>
    /// Идёт сохранение.
    /// </summary>
    [ObservableProperty]
    private bool _isSaving;

    /// <summary>
    /// Ошибка цены.
    /// </summary>
    [ObservableProperty]
    private string? _priceError;

    /// <summary>
    /// Ошибка длительности.
    /// </summary>
    [ObservableProperty]
    private string? _durationError;

    /// <summary>
    /// Общая ошибка формы: выбор услуги, отказ сервера.
    /// </summary>
    [ObservableProperty]
    private string? _formError;

    /// <summary>
    /// Доступные услуги справочника.
    /// </summary>
    public ObservableCollection<BarberServiceCatalogItem> Catalog { get; } = [];

    /// <summary>
    /// Кнопка сохранения доступна.
    /// </summary>
    public bool CanSave => !IsSaving;

    /// <summary>
    /// Задача текущего чтения справочника: тесты ждут её вместо таймера.
    /// </summary>
    public Task CatalogLoading => _catalogTask;

    /// <summary>
    /// Услуга сохранена: страница объявляет сообщение и перечитывает прайс.
    /// </summary>
    public event EventHandler<string>? Saved;

    /// <summary>
    /// Привязывает редактор к профилю.
    /// </summary>
    /// <param name="barberProfileId">Профиль мастера.</param>
    public void Initialize(Guid barberProfileId)
    {
        if (barberProfileId == Guid.Empty)
        {
            throw new ArgumentException(
                "Идентификатор профиля не может быть пустым.",
                nameof(barberProfileId));
        }

        BarberProfileId = barberProfileId;
    }

    /// <summary>
    /// Готовит добавление услуги: запоминает профиль, уже существующие строки прайса и
    /// специализации.
    /// </summary>
    /// <param name="barberProfileId">Профиль мастера.</param>
    /// <param name="existing">Текущий прайс: его услуги не предлагаются повторно.</param>
    /// <param name="attachedSpecializationIds">
    /// Специализации профиля: специализация выбранной услуги, которой здесь нет, добавляется
    /// вместе с услугой.
    /// </param>
    public void InitializeForAdd(
        Guid barberProfileId,
        IReadOnlyList<BarberServiceItem> existing,
        IReadOnlyCollection<Guid>? attachedSpecializationIds = null)
    {
        Initialize(barberProfileId);
        InitializeForAdd(existing, attachedSpecializationIds);
    }

    /// <summary>
    /// Готовит добавление услуги, если профиль уже привязан через <see cref="Initialize"/>.
    /// </summary>
    /// <param name="existing">Текущий прайс: его услуги не предлагаются повторно.</param>
    /// <param name="attachedSpecializationIds">
    /// Специализации профиля: специализация выбранной услуги, которой здесь нет, добавляется
    /// вместе с услугой.
    /// </param>
    public void InitializeForAdd(
        IReadOnlyList<BarberServiceItem> existing,
        IReadOnlyCollection<Guid>? attachedSpecializationIds = null)
    {
        ArgumentNullException.ThrowIfNull(existing);

        IsAddMode = true;
        IsEditMode = false;
        _editedService = null;
        ServiceName = string.Empty;
        SelectedCatalogService = null;
        PriceText = string.Empty;
        DurationText = string.Empty;
        ClearErrors();
        CatalogError = null;
        IsCatalogEmpty = false;

        _existingHairServiceIds.Clear();
        _attachedSpecializationIds.Clear();

        foreach (var item in existing)
        {
            _existingHairServiceIds.Add(item.HairServiceId);
        }

        if (attachedSpecializationIds is not null)
        {
            foreach (var specializationId in attachedSpecializationIds)
            {
                _attachedSpecializationIds.Add(specializationId);
            }
        }

        Catalog.Clear();

        _catalogTask = LoadCatalogAsync(CancellationToken.None);
    }

    /// <summary>
    /// Готовит правку строки прайса.
    /// </summary>
    /// <param name="barberProfileId">Профиль мастера.</param>
    /// <param name="item">Строка прайса.</param>
    public void InitializeForEdit(Guid barberProfileId, BarberServiceItem item)
    {
        Initialize(barberProfileId);
        InitializeForEdit(item);
    }

    /// <summary>
    /// Готовит правку строки прайса, если профиль уже привязан.
    /// </summary>
    /// <param name="item">Строка прайса.</param>
    public void InitializeForEdit(BarberServiceItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        IsAddMode = false;
        IsEditMode = true;
        _editedService = item;
        ServiceName = item.Name;
        SelectedCatalogService = null;
        PriceText = item.Price.ToString("0.##", CultureInfo.InvariantCulture);
        DurationText = item.DurationMinutes.ToString(CultureInfo.InvariantCulture);
        Catalog.Clear();
        IsCatalogEmpty = false;
        CatalogError = null;
        ClearErrors();
    }

    /// <summary>
    /// Сохраняет услугу: <c>POST</c> в режиме добавления, <c>PUT</c> в режиме правки.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача сохранения.</returns>
    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (IsSaving)
        {
            return;
        }

        if (!Validate(out var price, out var duration))
        {
            return;
        }

        if (BarberProfileId is not { } profileId)
        {
            FormError = SaveFailedText;

            return;
        }

        if (IsAddMode && SelectedCatalogService is null)
        {
            FormError = ChooseServiceText;

            return;
        }

        IsSaving = true;
        FormError = null;

        try
        {
            if (IsEditMode && _editedService is { } service)
            {
                await _profileApi.UpdateServiceAsync(
                    profileId,
                    service.HairServiceId,
                    new UpdateBarberProfileServiceRequest(price, duration),
                    cancellationToken);

                Saved?.Invoke(this, Loc.T("Цена обновлена."));
            }
            else
            {
                var selectedService = SelectedCatalogService!;

                // Специализация выбранной услуги появляется в профиле вместе с услугой:
                // отдельного шага «Добавить специализацию» больше нет. Сначала пишется она,
                // чтобы услуга не оказалась в прайсе без своей специализации.
                await EnsureSpecializationAttachedAsync(
                    profileId,
                    selectedService,
                    cancellationToken);

                await _profileApi.AddServiceAsync(
                    profileId,
                    new AddBarberProfileServiceRequest(
                        selectedService.Id,
                        price,
                        duration),
                    cancellationToken);

                Saved?.Invoke(this, Loc.T("Услуга добавлена."));
            }
        }
        catch (Exception exception)
        {
            ApplySaveFailure(exception);
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>
    /// Повторяет чтение справочника после ошибки.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача чтения.</returns>
    [RelayCommand]
    private async Task RetryCatalogAsync(CancellationToken cancellationToken)
    {
        CatalogError = null;

        await LoadCatalogAsync(cancellationToken);
    }

    /// <summary>
    /// Подставляет длительность по умолчанию при выборе услуги.
    /// </summary>
    /// <param name="value">Выбранная услуга; <c>null</c> — выбор снят.</param>
    partial void OnSelectedCatalogServiceChanged(BarberServiceCatalogItem? value)
    {
        if (value is not null && IsAddMode)
        {
            DurationText = value.DefaultDurationMinutes.ToString(CultureInfo.InvariantCulture);
            FormError = null;
        }
    }

    /// <summary>
    /// Пересчитывает доступность кнопки вместе с флагом сохранения.
    /// </summary>
    partial void OnIsSavingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Читает справочник и убирает услуги, уже стоящие в прайсе.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача чтения.</returns>
    public async Task LoadCatalogAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoadingCatalog)
        {
            return;
        }

        IsLoadingCatalog = true;
        CatalogError = null;
        IsCatalogEmpty = false;

        try
        {
            var items = await _searchApi.GetHairServicesAsync(cancellationToken);

            Catalog.Clear();

            foreach (var item in items)
            {
                if (!_existingHairServiceIds.Contains(item.Id))
                {
                    Catalog.Add(new BarberServiceCatalogItem(item));
                }
            }

            IsCatalogEmpty = Catalog.Count == 0;
        }
        catch (Exception exception)
        {
            var kind = ApiFailureClassifier.Classify(exception, cancellationToken);

            if (kind != ApiFailureKind.None)
            {
                CatalogError = BarberProfileText.ReadError(
                    exception,
                    kind,
                    CatalogFailedText);
            }
        }
        finally
        {
            IsLoadingCatalog = false;
        }
    }

    /// <summary>
    /// Добавляет специализацию выбранной услуги, если её ещё нет в профиле.
    /// </summary>
    /// <remarks>
    /// Повторное добавление сервер отверг бы кодом дубликата; он же приходит, когда
    /// специализацию успели добавить с другого устройства, и тогда услугу можно сохранять.
    /// Любой другой отказ прерывает сохранение: услуга не должна попасть в прайс без
    /// специализации, по которой её найдут в фильтре.
    /// </remarks>
    /// <param name="profileId">Профиль мастера.</param>
    /// <param name="service">Выбранная услуга справочника.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача записи.</returns>
    private async Task EnsureSpecializationAttachedAsync(
        Guid profileId,
        BarberServiceCatalogItem service,
        CancellationToken cancellationToken)
    {
        var specializationId = service.SpecializationId;

        if (specializationId == Guid.Empty
            || !_attachedSpecializationIds.Add(specializationId))
        {
            return;
        }

        try
        {
            await _profileApi.AddSpecializationAsync(
                profileId,
                specializationId,
                cancellationToken);
        }
        catch (BarberSearchApiException exception)
            when (exception.ErrorCodes.Contains("barberProfile.addSpecialization.duplicate"))
        {
            // Специализация уже есть на сервере: профиль догоняет состояние, услугу пишем.
        }
        catch
        {
            _attachedSpecializationIds.Remove(specializationId);
            throw;
        }
    }

    /// <summary>
    /// Проверяет цену и длительность.
    /// </summary>
    /// <param name="price">Разобранная цена.</param>
    /// <param name="duration">Разобранная длительность.</param>
    /// <returns><c>true</c>, если можно отправлять.</returns>
    private bool Validate(out decimal price, out int duration)
    {
        ClearErrors();

        price = 0m;
        duration = 0;

        var isValid = true;
        var priceText = PriceText.Trim();

        if (priceText.Length == 0)
        {
            PriceError = PriceRequiredText;
            isValid = false;
        }
        else if (!TryParsePrice(priceText, out price))
        {
            PriceError = PriceRangeText;
            isValid = false;
        }
        else if (CountDecimals(priceText) > 2)
        {
            PriceError = PriceDecimalsText;
            isValid = false;
        }

        if (!int.TryParse(
                DurationText.Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out duration)
            || duration < MinDurationMinutes
            || duration > MaxDurationMinutes)
        {
            DurationError = DurationRangeText;
            isValid = false;
        }

        return isValid;
    }

    /// <summary>
    /// Разбирает цену с точкой или запятой; диапазон — от нуля до верхней границы.
    /// </summary>
    private static bool TryParsePrice(string text, out decimal price)
    {
        var normalized = text.Replace(',', '.');

        return decimal.TryParse(
                normalized,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out price)
            && price >= 0m
            && price <= MaxPrice;
    }

    /// <summary>
    /// Считает знаки после запятой: больше двух сервер округлит молча.
    /// </summary>
    private static int CountDecimals(string text)
    {
        var separator = text.IndexOfAny(['.', ',']);

        return separator < 0 ? 0 : text.Length - separator - 1;
    }

    /// <summary>
    /// Разбирает отказ сервера по коду услуги.
    /// </summary>
    /// <param name="exception">Исключение API.</param>
    private void ApplySaveFailure(Exception exception)
    {
        if (exception is not BarberSearchApiException apiException)
        {
            FormError = SaveFailedText;

            return;
        }

        var code = apiException.ErrorCodes.Count > 0 ? apiException.ErrorCodes[0] : null;

        FormError = code switch
        {
            "barberProfile.addService.duplicate" => Loc.T("Эта услуга уже есть в прайсе."),
            "barberProfile.updateService.notFound" => Loc.T("Эта услуга уже скрыта. Обновите страницу."),
            "barberProfile.removeService.notFound" => Loc.T("Эта услуга уже скрыта. Обновите страницу."),
            "barberProfile.addService.hairServiceNotFound" => Loc.T("Услуга справочника не найдена."),
            "barberProfile.addService.price" => Loc.T("Цена не может быть отрицательной."),
            "barberService.updatePrice" => Loc.T("Цена не может быть отрицательной."),
            "barberProfile.addService.duration" => Loc.T("Длительность — от 1 до 1440 минут."),
            "barberService.updateDuration" => Loc.T("Длительность — от 1 до 1440 минут."),
            "barberProfile.addSpecialization.specializationNotFound" => Loc.T("Специализация не найдена."),
            _ => apiException.StatusCode switch
            {
                HttpStatusCode.Forbidden => Loc.T("Прайс доступен только владельцу профиля."),
                HttpStatusCode.NotFound => Loc.T("Услуга не найдена. Обновите прайс."),
                _ => BarberProfileText.WriteError(apiException, SaveFailedText),
            },
        };
    }

    /// <summary>
    /// Сбрасывает ошибки полей перед новой проверкой.
    /// </summary>
    private void ClearErrors()
    {
        PriceError = null;
        DurationError = null;
        FormError = null;
    }
}

/// <summary>
/// Строка справочника услуг в редакторе прайса.
/// </summary>
public sealed class BarberServiceCatalogItem
{
    /// <summary>
    /// Создаёт строку из услуги справочника.
    /// </summary>
    /// <param name="service">Услуга из публичного справочника.</param>
    public BarberServiceCatalogItem(HairServiceResponse service)
    {
        ArgumentNullException.ThrowIfNull(service);

        Id = service.Id;
        Name = service.Name;
        CategoryText = BarberProfileText.CategoryText(service.Category);
        SpecializationId = service.SpecializationId;
        DefaultDurationMinutes = service.DefaultDurationMinutes;
    }

    /// <summary>
    /// Идентификатор услуги справочника.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Специализация услуги: её редактор добавляет в профиль вместе с услугой.
    /// </summary>
    public Guid SpecializationId { get; }

    /// <summary>
    /// Название услуги.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Категория для показа: «Стрижка».
    /// </summary>
    public string CategoryText { get; }

    /// <summary>
    /// Длительность по умолчанию: её редактор подставляет в поле.
    /// </summary>
    public int DefaultDurationMinutes { get; }

    /// <summary>
    /// Длительность по умолчанию строкой: «45 мин».
    /// </summary>
    public string DurationText => BarberProfileText.DurationText(DefaultDurationMinutes);
}

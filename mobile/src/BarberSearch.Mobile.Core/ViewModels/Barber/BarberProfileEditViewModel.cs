using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.UpdateBarberProfile;
using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Barber;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Правка публичной карточки мастера: имя, опыт, языки, оплата и свободные списки.
/// </summary>
/// <remarks>
/// <para>
/// Форма повторяет веб (<c>barber-profile-form.tsx</c>, режим <c>edit</c>): грейд только
/// для чтения — его меняет администратор; списки вводятся по одному пункту в строке и
/// сохраняются уже очищенными. Тело заменяет карточку целиком, поэтому сюда попадают все
/// поля, включая пустые: иначе сохранение одного имени стёрло бы описание и прайс.
/// </para>
/// <para>
/// Клиентские границы совпадают с доменом: имя 1..120, опыт 1..70, хотя бы один язык,
/// описание до 2000, правило опозданий до 500, пункт списка до 500. Сервер остаётся
/// источником истины: его коды раскладываются по тем же полям, а не показываются общей
/// строкой. <c>latePolicy</c> уходит пустой строкой, а не <c>null</c>, — таков контракт.
/// </para>
/// </remarks>
public sealed partial class BarberProfileEditViewModel : ObservableObject
{
    private const int MaxDisplayNameLength = 120;
    private const int MinExperienceYears = 1;
    private const int MaxExperienceYears = 70;
    private const int MaxBioLength = 2000;
    private const int MaxLatePolicyLength = 500;
    private const int MaxListItemLength = 500;

    private static string DisplayNameRequiredText => Loc.T("Укажите, как вас представить.");
    private static string DisplayNameTooLongText => Loc.T("Не длиннее 120 символов.");
    private static string ExperienceRequiredText => Loc.T("Укажите опыт работы в годах.");
    private static string ExperienceRangeText => Loc.T("Опыт — от 1 до 70 лет.");
    private static string LanguagesRequiredText => Loc.T("Выберите хотя бы один язык.");
    private static string BioTooLongText => Loc.T("О себе — не длиннее 2000 символов.");
    private static string LatePolicyTooLongText => Loc.T("Правило опозданий — не длиннее 500 символов.");
    private static string ListItemTooLongText => Loc.T("Пункт списка — не длиннее 500 символов.");
    private static string SaveFailedText => Loc.T("Не удалось сохранить профиль. Попробуйте ещё раз.");
    private static string ForbiddenText => Loc.T("Нет прав на изменение профиля.");

    private readonly IBarberProfileApi _profileApi;

    private BarberProfileResponse? _profile;

    /// <summary>
    /// Создаёт форму правки.
    /// </summary>
    /// <param name="profileApi">Сохранение карточки.</param>
    /// <param name="autoRetry">
    /// Цикл автоповтора, зарезервированный композиционным корнем. Форма не делает фоновых
    /// чтений, а <c>PUT</c> не повторяется вслепую: запись уже могла дойти до сервера.
    /// Параметр оставлен, чтобы подпись совпадала с остальными разделами кабинета.
    /// </param>
    public BarberProfileEditViewModel(
        IBarberProfileApi profileApi,
        AutoRetryLoop? autoRetry = null)
    {
        ArgumentNullException.ThrowIfNull(profileApi);

        _profileApi = profileApi;
    }

    /// <summary>
    /// Как представить мастера клиенту.
    /// </summary>
    [ObservableProperty]
    private string _displayName = string.Empty;

    /// <summary>
    /// Опыт в годах строкой: поле ввода не умеет хранить число.
    /// </summary>
    [ObservableProperty]
    private string _experienceYears = string.Empty;

    /// <summary>
    /// Короткое описание о себе.
    /// </summary>
    [ObservableProperty]
    private string _bio = string.Empty;

    /// <summary>
    /// Правило опозданий; пустая строка допустима.
    /// </summary>
    [ObservableProperty]
    private string _latePolicy = string.Empty;

    /// <summary>
    /// Сертификаты и обучение: по одному пункту в строке.
    /// </summary>
    [ObservableProperty]
    private string _certificates = string.Empty;

    /// <summary>
    /// Достижения: по одному пункту в строке.
    /// </summary>
    [ObservableProperty]
    private string _achievements = string.Empty;

    /// <summary>
    /// Формат общения: по одному пункту в строке.
    /// </summary>
    [ObservableProperty]
    private string _communicationFormat = string.Empty;

    /// <summary>
    /// Комфорт в кресле: по одному пункту в строке.
    /// </summary>
    [ObservableProperty]
    private string _goodies = string.Empty;

    /// <summary>
    /// Дополнительные услуги: по одному пункту в строке.
    /// </summary>
    [ObservableProperty]
    private string _additionalServices = string.Empty;

    /// <summary>
    /// Идёт сохранение: кнопка показывает индикатор.
    /// </summary>
    [ObservableProperty]
    private bool _isSaving;

    /// <summary>
    /// Ошибка поля «Как вас представить».
    /// </summary>
    [ObservableProperty]
    private string? _displayNameError;

    /// <summary>
    /// Ошибка поля «Опыт, лет».
    /// </summary>
    [ObservableProperty]
    private string? _experienceError;

    /// <summary>
    /// Ошибка выбора языков.
    /// </summary>
    [ObservableProperty]
    private string? _languagesError;

    /// <summary>
    /// Ошибка выбора способов оплаты.
    /// </summary>
    [ObservableProperty]
    private string? _paymentMethodsError;

    /// <summary>
    /// Ошибка поля «О себе».
    /// </summary>
    [ObservableProperty]
    private string? _bioError;

    /// <summary>
    /// Ошибка поля «Опоздания».
    /// </summary>
    [ObservableProperty]
    private string? _latePolicyError;

    /// <summary>
    /// Общая ошибка формы или ошибка грейда.
    /// </summary>
    [ObservableProperty]
    private string? _formError;

    /// <summary>
    /// Грейд только для чтения: его назначает администратор.
    /// </summary>
    [ObservableProperty]
    private string _gradeText = string.Empty;

    /// <summary>
    /// Языки общения с отметками выбора.
    /// </summary>
    public ObservableCollection<BarberLanguageOption> Languages { get; } = [];

    /// <summary>
    /// Способы оплаты с отметками выбора.
    /// </summary>
    public ObservableCollection<BarberPaymentOption> PaymentMethods { get; } = [];

    /// <summary>
    /// Форма сохранена: страница объявляет успех и перечитывает карточку.
    /// </summary>
    public event EventHandler? Saved;

    /// <summary>
    /// Кнопка сохранения доступна.
    /// </summary>
    public bool CanSave => !IsSaving;

    /// <summary>
    /// Заполняет форму из карточки.
    /// </summary>
    /// <remarks>
    /// Вызывается один раз после чтения профиля: повторный вызов перезапишет правки,
    /// сделанные человеком, поэтому страница создаёт форму заново после сохранения.
    /// </remarks>
    /// <param name="profile">Собственная карточка мастера.</param>
    public void Initialize(BarberProfileResponse profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        _profile = profile;

        DisplayName = profile.DisplayName;
        ExperienceYears = profile.YearsOfExperience.ToString(CultureInfo.InvariantCulture);
        Bio = profile.Bio ?? string.Empty;
        LatePolicy = profile.LatePolicy;
        Certificates = string.Join(Environment.NewLine, profile.CertificatesAndTraining);
        Achievements = string.Join(Environment.NewLine, profile.Achievements);
        CommunicationFormat = string.Join(Environment.NewLine, profile.CommunicationFormat);
        Goodies = string.Join(Environment.NewLine, profile.Goodies);
        AdditionalServices = string.Join(Environment.NewLine, profile.AdditionalServices);
        GradeText = BarberProfileText.GradeText(profile.Grade);

        BuildLanguages(profile.Languages);
        BuildPaymentMethods(profile.PaymentMethods);

        ClearErrors();
    }

    /// <summary>
    /// Сохраняет карточку целиком.
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

        if (_profile is not { } profile)
        {
            FormError = SaveFailedText;

            return;
        }

        if (!Validate(out var years))
        {
            return;
        }

        IsSaving = true;
        FormError = null;

        try
        {
            await _profileApi.UpdateAsync(
                profile.Id,
                BuildRequest(profile, years),
                cancellationToken);

            Saved?.Invoke(this, EventArgs.Empty);
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
    /// Пересчитывает доступность кнопки вместе с флагом сохранения.
    /// </summary>
    partial void OnIsSavingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Проверяет поля и раскладывает ошибки по ним.
    /// </summary>
    /// <param name="years">Разобранный опыт, если проверка прошла.</param>
    /// <returns><c>true</c>, если можно отправлять.</returns>
    private bool Validate(out int years)
    {
        ClearErrors();

        var name = DisplayName.Trim();
        var isValid = true;

        if (name.Length == 0)
        {
            DisplayNameError = DisplayNameRequiredText;
            isValid = false;
        }
        else if (name.Length > MaxDisplayNameLength)
        {
            DisplayNameError = DisplayNameTooLongText;
            isValid = false;
        }

        years = 0;

        if (!int.TryParse(
                ExperienceYears.Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            ExperienceError = ExperienceRequiredText;
            isValid = false;
        }
        else if (parsed < MinExperienceYears || parsed > MaxExperienceYears)
        {
            ExperienceError = ExperienceRangeText;
            isValid = false;
        }
        else
        {
            years = parsed;
        }

        if (!Languages.Any(language => language.IsSelected))
        {
            LanguagesError = LanguagesRequiredText;
            isValid = false;
        }

        if (Bio.Trim().Length > MaxBioLength)
        {
            BioError = BioTooLongText;
            isValid = false;
        }

        if (LatePolicy.Trim().Length > MaxLatePolicyLength)
        {
            LatePolicyError = LatePolicyTooLongText;
            isValid = false;
        }

        if (AllListValues().Any(value => value.Length > MaxListItemLength))
        {
            FormError = ListItemTooLongText;
            isValid = false;
        }

        return isValid;
    }

    /// <summary>
    /// Собирает тело замены карточки.
    /// </summary>
    /// <param name="profile">Текущая карточка: нужен идентификатор.</param>
    /// <param name="years">Проверенный опыт.</param>
    /// <returns>Полное новое состояние профиля.</returns>
    private BarberProfileUpdateRequest BuildRequest(
        BarberProfileResponse profile,
        int years)
    {
        _ = profile;

        return new BarberProfileUpdateRequest(
            DisplayName.Trim(),
            years,
            string.IsNullOrWhiteSpace(Bio) ? null : Bio.Trim(),
            Languages.Where(option => option.IsSelected).Select(option => option.Language).ToList(),
            ToLines(Certificates),
            ToLines(Achievements),
            PaymentMethods
                .Where(option => option.IsSelected)
                .Select(option => option.Method)
                .ToList(),
            ToLines(CommunicationFormat),
            ToLines(Goodies),
            LatePolicy.Trim(),
            ToLines(AdditionalServices));
    }

    /// <summary>
    /// Разбирает серверный отказ: коды полей важнее общей строки.
    /// </summary>
    /// <param name="exception">Исключение API.</param>
    private void ApplySaveFailure(Exception exception)
    {
        if (exception is not BarberSearchApiException apiException)
        {
            FormError = SaveFailedText;

            return;
        }

        foreach (var code in apiException.ErrorCodes)
        {
            switch (code)
            {
                case "barberProfile.displayName":
                    DisplayNameError = DisplayNameRequiredText;
                    return;
                case "barberProfile.displayName.tooLong":
                    DisplayNameError = DisplayNameTooLongText;
                    return;
                case "barberProfile.yearsOfExperience":
                    ExperienceError = ExperienceRangeText;
                    return;
                case "barberProfile.grade.unknown":
                    FormError = Loc.T("Выберите грейд из списка.");
                    return;
                case "barberProfile.languages.unknown":
                    LanguagesError = Loc.T("Выберите языки из списка.");
                    return;
                case "barberProfile.paymentMethods.unknown":
                    PaymentMethodsError = Loc.T("Выберите способы оплаты из списка.");
                    return;
            }
        }

        FormError = apiException.StatusCode switch
        {
            HttpStatusCode.Forbidden => ForbiddenText,
            _ => BarberProfileText.WriteError(apiException, SaveFailedText),
        };
    }

    /// <summary>
    /// Сбрасывает все ошибки перед новой проверкой или после успеха.
    /// </summary>
    private void ClearErrors()
    {
        DisplayNameError = null;
        ExperienceError = null;
        LanguagesError = null;
        PaymentMethodsError = null;
        BioError = null;
        LatePolicyError = null;
        FormError = null;
    }

    /// <summary>
    /// Собирает все строки списков — для проверки длины пункта.
    /// </summary>
    private IEnumerable<string> AllListValues() =>
        new[]
        {
            Certificates,
            Achievements,
            CommunicationFormat,
            Goodies,
            AdditionalServices,
        }.SelectMany(ToLines);

    /// <summary>
    /// «По одному пункту в строке»: обрезает и выбрасывает пустые строки.
    /// </summary>
    private static List<string> ToLines(string value) =>
        (value ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Length > 0)
            .ToList();

    /// <summary>
    /// Строит список языков в порядке словаря, отмечая выбранные.
    /// </summary>
    private void BuildLanguages(IReadOnlyList<Language> selected)
    {
        Languages.Clear();

        var chosen = selected.ToHashSet();

        foreach (var language in BarberLanguageOption.Order)
        {
            Languages.Add(new BarberLanguageOption(language, chosen.Contains(language)));
        }
    }

    /// <summary>
    /// Строит список способов оплаты в порядке словаря, отмечая выбранные.
    /// </summary>
    private void BuildPaymentMethods(IReadOnlyList<PaymentMethod> selected)
    {
        PaymentMethods.Clear();

        var chosen = selected.ToHashSet();

        foreach (var method in BarberPaymentOption.Order)
        {
            PaymentMethods.Add(new BarberPaymentOption(method, chosen.Contains(method)));
        }
    }
}

/// <summary>
/// Язык общения с отметкой выбора.
/// </summary>
public sealed partial class BarberLanguageOption : ObservableObject
{
    /// <summary>
    /// Порядок языков в форме: как в словаре, а не как пришло из JSON.
    /// </summary>
    public static readonly IReadOnlyList<Language> Order =
    [
        Language.Ru,
        Language.En,
        Language.De,
        Language.Kg,
        Language.Tj,
        Language.Uz,
    ];

    /// <summary>
    /// Создаёт строку выбора языка.
    /// </summary>
    /// <param name="language">Язык.</param>
    /// <param name="isSelected">Выбран ли он в карточке.</param>
    public BarberLanguageOption(Language language, bool isSelected)
    {
        Language = language;
        Text = BarberProfileText.LanguageText(language);
        _isSelected = isSelected;
    }

    /// <summary>
    /// Значение языка в API.
    /// </summary>
    public Language Language { get; }

    /// <summary>
    /// Подпись на экране: «Русский», «English».
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Язык выбран.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// Способ оплаты с отметкой выбора.
/// </summary>
public sealed partial class BarberPaymentOption : ObservableObject
{
    /// <summary>
    /// Порядок способов оплаты в форме: как в словаре.
    /// </summary>
    public static readonly IReadOnlyList<PaymentMethod> Order =
    [
        PaymentMethod.Cash,
        PaymentMethod.Card,
        PaymentMethod.DigitalWallet,
        PaymentMethod.Sbp,
        PaymentMethod.BankTransfer,
    ];

    /// <summary>
    /// Создаёт строку выбора способа оплаты.
    /// </summary>
    /// <param name="method">Способ оплаты.</param>
    /// <param name="isSelected">Выбран ли он в карточке.</param>
    public BarberPaymentOption(PaymentMethod method, bool isSelected)
    {
        Method = method;
        Text = BarberProfileText.PaymentText(method);
        _isSelected = isSelected;
    }

    /// <summary>
    /// Значение способа оплаты в API.
    /// </summary>
    public PaymentMethod Method { get; }

    /// <summary>
    /// Подпись на экране: «Наличные», «СБП».
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Способ оплаты выбран.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;
}

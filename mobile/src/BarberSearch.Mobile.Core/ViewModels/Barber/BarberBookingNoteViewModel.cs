using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Barber;
using BarberSearch.Mobile.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Экран «Заметка к записи» кабинета мастера: текст до тысячи символов и сохранение.
/// </summary>
/// <remarks>
/// <para>
/// Экран повторяет веб (<c>frontend/src/components/barber/barber-note-dialog.tsx</c>):
/// заметку видит только мастер, а пустой текст её очищает — так же, как это делает сервер,
/// который сам обрезает пробелы по краям. Поэтому сюда не переезжают ни причина отмены, ни
/// комментарий клиента: это разные поля разных экранов.
/// </para>
/// <para>
/// Начальное значение приходит из строки записи: тот же текст открывается повторно, и
/// «Сохранить» без правок ничего не меняет. Успех поднимает <see cref="Saved"/> — страница
/// применяет заметку к строке владельца (<see cref="BarberBookingsViewModel.ApplyNote"/>)
/// и закрывается, а баннер «Заметка сохранена.» показывает уже список.
/// </para>
/// <para>
/// Сохранение — запись, поэтому автоповтора у неё нет: после сбоя исход неизвестен, и
/// человек сам решает, повторить ли действие. Причина показывается по кодам
/// <c>errors.ts</c>: слишком длинная заметка, чужой или исчезнувший визит.
/// </para>
/// </remarks>
public sealed partial class BarberBookingNoteViewModel : ObservableObject
{
    /// <summary>
    /// Предел заметки: совпадает с серверным <c>booking.barberNote.tooLong</c>.
    /// </summary>
    public const int MaxNoteLength = 1000;

    private static string SaveFailedText => Loc.T("Не удалось сохранить заметку.");
    private static string TooLongText => Loc.T("Слишком длинная заметка — максимум 1000 символов.");

    private readonly IBarberBookingsApi _bookingsApi;

    private BarberBookingItem? _item;

    /// <summary>
    /// Создаёт экран заметки.
    /// </summary>
    /// <param name="bookingsApi">Сохранение заметки мастера.</param>
    public BarberBookingNoteViewModel(IBarberBookingsApi bookingsApi)
    {
        ArgumentNullException.ThrowIfNull(bookingsApi);

        _bookingsApi = bookingsApi;
    }

    /// <summary>
    /// Заметка сохранена: страница применяет текст к строке и закрывается.
    /// </summary>
    /// <remarks>
    /// Событие несёт уже обрезанный текст — ровно тот, что ушёл на сервер, — поэтому
    /// страница не решает, что считать заметкой, и не повторяет <c>PUT</c>.
    /// </remarks>
    public event EventHandler<string>? Saved;

    /// <summary>
    /// Текст заметки; начальное значение — заметка из строки записи.
    /// </summary>
    [ObservableProperty]
    private string _note = string.Empty;

    /// <summary>
    /// Имя клиента из строки записи.
    /// </summary>
    [ObservableProperty]
    private string _customerName = string.Empty;

    /// <summary>
    /// Момент визита в поясе салона: «сб, 15 ноября в 18:30».
    /// </summary>
    [ObservableProperty]
    private string _momentText = string.Empty;

    /// <summary>
    /// Идёт сохранение: кнопка показывает индикатор и не нажимается дважды.
    /// </summary>
    [ObservableProperty]
    private bool _isSaving;

    /// <summary>
    /// Ошибка сохранения; <c>null</c>, когда её нет.
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Строка записи принята: экран можно показывать.
    /// </summary>
    public bool IsInitialized => _item is not null;

    /// <summary>
    /// Счётчик текста: «0 / 1000».
    /// </summary>
    public string CounterText => Loc.T("{0} / {1}", Note.Length, MaxNoteLength);

    /// <summary>
    /// Текст длиннее серверного предела.
    /// </summary>
    public bool IsTooLong => Note.Length > MaxNoteLength;

    /// <summary>
    /// Подсказка о слишком длинном тексте; пустая, пока предел не превышен.
    /// </summary>
    public string LengthError => IsTooLong ? TooLongText : string.Empty;

    /// <summary>
    /// Можно ли сохранить: запись известна, сохранения нет и предел текста не превышен.
    /// </summary>
    public bool CanSave => IsInitialized && !IsSaving && !IsTooLong;

    /// <summary>
    /// Надпись на кнопке: во время запроса — «Сохраняем…».
    /// </summary>
    public string SubmitText => IsSaving ? Loc.T("Сохраняем…") : Loc.T("Сохранить");

    /// <summary>
    /// Есть ошибка сохранения.
    /// </summary>
    public bool HasError => ErrorMessage is not null;

    /// <summary>
    /// Принимает строку записи и подставляет её заметку.
    /// </summary>
    /// <remarks>
    /// Пустая заметка — обычное состояние: поле остаётся пустым, а сохранение очистит
    /// заметку на сервере. Текст не обрезается при открытии: человек вправе видеть ровно
    /// то, что сохранено, а обрезка происходит при сохранении.
    /// </remarks>
    /// <param name="item">Строка списка записей.</param>
    public void Initialize(BarberBookingItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        _item = item;

        CustomerName = item.CustomerName;
        MomentText = item.MomentText;
        Note = item.Booking?.BarberNote ?? string.Empty;
        ErrorMessage = null;

        NotifyViewState();
    }

    /// <summary>
    /// Сохраняет заметку и сообщает странице её текст.
    /// </summary>
    /// <remarks>
    /// Текст обрезается по краям: пробелы — не заметка. Пустая строка — законное значение,
    /// она очищает заметку. Ошибки разбираются по кодам: <c>429</c>, истёкшая сессия,
    /// чужой визит и слишком длинный текст получают свои подписи.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены страницы.</param>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var item = _item;

        if (IsSaving || item is null || IsTooLong)
        {
            return;
        }

        var note = Note.Trim();

        IsSaving = true;
        ErrorMessage = null;

        try
        {
            await _bookingsApi.SaveNoteAsync(item.Id, note, cancellationToken);

            Saved?.Invoke(this, note);
        }
        catch (BarberSearchApiException exception)
        {
            ErrorMessage = BarberCabinetText.ActionError(exception, SaveFailedText);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Страница ушла: исход сохранения неизвестен, повторять его молча нельзя.
        }
        catch (Exception exception)
        {
            ErrorMessage = BarberCabinetText.ReadError(exception, SaveFailedText);
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>
    /// Поднимает уведомления обо всём, что зависит от текста и сохранения.
    /// </summary>
    private void NotifyViewState()
    {
        OnPropertyChanged(nameof(IsInitialized));
        OnPropertyChanged(nameof(CounterText));
        OnPropertyChanged(nameof(IsTooLong));
        OnPropertyChanged(nameof(LengthError));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(SubmitText));
        OnPropertyChanged(nameof(HasError));

        SaveCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Текст изменился: счётчик, предел и доступность кнопки.
    /// </summary>
    /// <param name="value">Новый текст.</param>
    partial void OnNoteChanged(string value)
    {
        if (ErrorMessage is not null)
        {
            ErrorMessage = null;
        }

        NotifyViewState();
    }

    /// <summary>
    /// Сохранение началось или закончилось: кнопка меняет подпись.
    /// </summary>
    /// <param name="value">Идёт сохранение.</param>
    partial void OnIsSavingChanged(bool value) => NotifyViewState();

    /// <summary>
    /// Ошибка сохранения появилась или исчезла.
    /// </summary>
    /// <param name="value">Текст ошибки.</param>
    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));
}

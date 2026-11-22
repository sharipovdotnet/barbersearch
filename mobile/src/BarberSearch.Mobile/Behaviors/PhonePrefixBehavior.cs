using System.ComponentModel;
using BarberSearch.Mobile.Core.Formatting;

namespace BarberSearch.Mobile.Behaviors;

/// <summary>
/// Держит в поле «Телефон» нестираемое начало <c>+79</c> и восстанавливает курсор после
/// каждой нормализации текста.
/// </summary>
/// <remarks>
/// <para>
/// Поле на телефоне — обычный <c>Entry</c> без маски, поэтому всю работу делает связка
/// «текст → <see cref="PhoneInput.NormalizeMobile"/> → текст и курсор». Начало <c>+79</c>
/// состоит из кода страны и обязательной девятки, так что в поле всегда стоит скелет
/// валидного российского мобильного, а в ViewModel уходит каноническое значение
/// <c>+79991234567</c> — его принимает серверное правило <c>PhoneNumber</c>.
/// </para>
/// <para>
/// Курсор нельзя оставить на месте после переписывания: ViewModel и поле обмениваются
/// текстом через привязку, и без явной установки <c>CursorPosition</c> он прыгал бы к
/// началу строки. Поэтому каждая замена текста заканчивается отложенной установкой курсора
/// по числу цифр слева от него, а позиции внутри <c>+79</c> запрещены вовсе — иначе
/// человек мог бы напечатать цифру между <c>+</c> и <c>7</c> и получить невалидный номер.
/// </para>
/// <para>
/// Пустое поле при получении фокуса сразу показывает <c>+79</c>: набранное с клавиатуры
/// дописывается после него, а стереть начало нельзя ни <c>Backspace</c>, ни <c>Delete</c>,
/// ни выделением всего текста. Без фокуса поле остаётся пустым и показывает подсказку с
/// примером номера — как до появления начала: <c>+79</c> без единой набранной цифры при
/// потере фокуса убирается, чтобы подсказка вернулась.
/// </para>
/// </remarks>
public sealed class PhonePrefixBehavior : Behavior<Entry>
{
    private Entry? _entry;
    private bool _isNormalizing;
    private bool _isRestoringCaret;

    /// <summary>
    /// Подписывается на события поля: начало номера подставляется при получении фокуса.
    /// </summary>
    /// <param name="bindable">Поле «Телефон».</param>
    protected override void OnAttachedTo(Entry bindable)
    {
        base.OnAttachedTo(bindable);

        _entry = bindable;

        bindable.TextChanged += OnTextChanged;
        bindable.Focused += OnFocused;
        bindable.Unfocused += OnUnfocused;
        bindable.PropertyChanged += OnEntryPropertyChanged;
    }

    /// <summary>
    /// Отписывается от событий поля.
    /// </summary>
    /// <param name="bindable">Поле «Телефон».</param>
    protected override void OnDetachingFrom(Entry bindable)
    {
        bindable.TextChanged -= OnTextChanged;
        bindable.Focused -= OnFocused;
        bindable.Unfocused -= OnUnfocused;
        bindable.PropertyChanged -= OnEntryPropertyChanged;

        _entry = null;

        base.OnDetachingFrom(bindable);
    }

    /// <summary>
    /// Поле получило фокус: пустое поле показывает начало номера, курсор уходит за него.
    /// </summary>
    /// <param name="sender">Поле «Телефон».</param>
    /// <param name="e">Параметры события фокуса.</param>
    private void OnFocused(object? sender, FocusEventArgs e)
    {
        if (_entry is not { } entry)
        {
            return;
        }

        EnsurePrefix(entry);
        MoveCaretAfterPrefix(entry);
    }

    /// <summary>
    /// Поле потеряло фокус: начало без набранных цифр убирается, чтобы вернулась подсказка
    /// с примером номера.
    /// </summary>
    /// <param name="sender">Поле «Телефон».</param>
    /// <param name="e">Параметры события фокуса.</param>
    private void OnUnfocused(object? sender, FocusEventArgs e)
    {
        if (_entry is not { } entry || PhoneInput.HasNationalDigits(entry.Text))
        {
            return;
        }

        // Пустой текст TextChanged пропускает, пока поле не в фокусе, поэтому начало не
        // вернётся само и в поле останется подсказка, как до этого изменения.
        _isNormalizing = true;

        try
        {
            entry.Text = string.Empty;
        }
        finally
        {
            _isNormalizing = false;
        }
    }

    /// <summary>
    /// Текст поля изменился: значение нормализуется, курсор ставится после цифр.
    /// </summary>
    /// <param name="sender">Поле «Телефон».</param>
    /// <param name="e">Старый и новый текст поля.</param>
    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isNormalizing || _entry is not { } entry)
        {
            return;
        }

        // Пустое поле без фокуса — это подсказка с примером номера, а не начало ввода:
        // нормализация вернула бы сюда +79 и подсказка бы не показывалась.
        if (!entry.IsFocused && string.IsNullOrEmpty(e.NewTextValue))
        {
            return;
        }

        _isNormalizing = true;

        try
        {
            var edit = PhoneInput.NormalizeMobile(e.NewTextValue, entry.CursorPosition);

            if (!string.Equals(edit.Text, e.NewTextValue, StringComparison.Ordinal))
            {
                // Присваивание поднимает TextChanged ещё раз — его гасит _isNormalizing;
                // курсор ставится отдельно, уже после того как платформа применит текст.
                entry.Text = edit.Text;
                RestoreCaret(entry, edit.CaretPosition);

                return;
            }
        }
        finally
        {
            _isNormalizing = false;
        }

        if (entry.SelectionLength == 0 && entry.CursorPosition < PhoneInput.MobilePrefixLength)
        {
            RestoreCaret(entry, PhoneInput.MobilePrefixLength);
        }
    }

    /// <summary>
    /// Курсор сдвинулся: позиции внутри <c>+79</c> запрещены, пока ничего не выделено.
    /// </summary>
    /// <param name="sender">Поле «Телефон».</param>
    /// <param name="e">Имя изменившегося свойства.</param>
    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isRestoringCaret || _entry is not { } entry)
        {
            return;
        }

        if (!string.Equals(e.PropertyName, nameof(Entry.CursorPosition), StringComparison.Ordinal))
        {
            return;
        }

        var text = entry.Text;

        if (text is null
            || !text.StartsWith(PhoneInput.MobilePrefix, StringComparison.Ordinal)
            || entry.SelectionLength != 0
            || entry.CursorPosition >= PhoneInput.MobilePrefixLength)
        {
            return;
        }

        // Ставим сразу, а не через Dispatcher: следующая цифра должна попасть уже после
        // начала, иначе между + и 7 окажется лишний символ.
        _isRestoringCaret = true;

        try
        {
            entry.CursorPosition = PhoneInput.MobilePrefixLength;
        }
        finally
        {
            _isRestoringCaret = false;
        }
    }

    /// <summary>
    /// Подставляет <c>+79</c>, если поле пустое; непустое значение не трогает.
    /// </summary>
    /// <param name="entry">Поле «Телефон».</param>
    private void EnsurePrefix(Entry entry)
    {
        if (!string.IsNullOrEmpty(entry.Text))
        {
            return;
        }

        var edit = PhoneInput.NormalizeMobile(string.Empty, 0);

        _isNormalizing = true;

        try
        {
            entry.Text = edit.Text;
        }
        finally
        {
            _isNormalizing = false;
        }

        RestoreCaret(entry, edit.CaretPosition);
    }

    /// <summary>
    /// Ставит курсор после <c>+79</c>, если он оказался внутри начала номера.
    /// </summary>
    /// <param name="entry">Поле «Телефон».</param>
    private void MoveCaretAfterPrefix(Entry entry)
    {
        var text = entry.Text;

        if (text is null
            || !text.StartsWith(PhoneInput.MobilePrefix, StringComparison.Ordinal)
            || entry.SelectionLength != 0
            || entry.CursorPosition >= PhoneInput.MobilePrefixLength)
        {
            return;
        }

        RestoreCaret(entry, PhoneInput.MobilePrefixLength);
    }

    /// <summary>
    /// Откладывает установку курсора до конца текущего изменения текста.
    /// </summary>
    /// <param name="entry">Поле «Телефон».</param>
    /// <param name="caretPosition">Позиция после нормализованного текста.</param>
    private void RestoreCaret(Entry entry, int caretPosition)
    {
        entry.Dispatcher.Dispatch(() =>
        {
            // Поле могли отцепить или заменить, пока задача ждала очереди.
            if (!ReferenceEquals(_entry, entry) || entry.Text is not { } text
                || text.Length < PhoneInput.MobilePrefixLength)
            {
                return;
            }

            _isRestoringCaret = true;

            try
            {
                entry.SelectionLength = 0;
                entry.CursorPosition = Math.Clamp(
                    caretPosition,
                    PhoneInput.MobilePrefixLength,
                    text.Length);
            }
            finally
            {
                _isRestoringCaret = false;
            }
        });
    }
}

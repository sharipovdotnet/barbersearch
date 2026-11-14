using BarberSearch.Contracts.Users.Me;
using BarberSearch.Domain.Common;
using BarberSearch.Domain.Users;
using BarberSearch.Mobile.Core.Formatting;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки текстов аккаунта: роль, полное имя, инициалы и служебные моменты профиля.
/// </summary>
/// <remarks>
/// <para>
/// Строки сверяются дословно с вебом (<c>frontend/src/lib/my-profile.ts</c>,
/// <c>ACCOUNT_ROLE_LABELS</c> и <c>profileDisplayName</c>): это одна копия текста на два
/// клиента, и расхождение здесь — не опечатка, а разная подпись одного продукта.
/// </para>
/// <para>
/// Пояс передаётся параметром, поэтому результат не зависит от часового пояса машины:
/// используется фиксированный +03:00, где 15 ноября 2026 — воскресенье.
/// </para>
/// </remarks>
public sealed class AccountTextTests
{
    /// <summary>
    /// Фиксированный пояс +03:00: результат не зависит от пояса машины.
    /// </summary>
    private static readonly TimeZoneInfo PlusThreeHours = TimeZoneInfo.CreateCustomTimeZone(
        "Test",
        TimeSpan.FromHours(3),
        "Test",
        "Test");

    /// <summary>
    /// 15 ноября 2026, 15:30 UTC — это 18:30 в поясе +03:00.
    /// </summary>
    private static readonly DateTimeOffset Instant = new(2026, 11, 15, 15, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// Момент в поясе +03:00: воскресенье, 15 ноября, 18:30.
    /// </summary>
    private const string ExpectedMoment = "вс, 15 ноября в 18:30";

    [Theory]
    [InlineData(UserRole.Customer, "Клиент")]
    [InlineData(UserRole.Barber, "Барбер")]
    [InlineData(UserRole.Admin, "Администратор")]
    public void RoleText_KnownRole_ShowsRussianCaption(UserRole role, string expected) =>
        Assert.Equal(expected, AccountText.RoleText(role));

    [Fact]
    public void RoleText_UnknownRole_FallsBackToEnumName() =>
        // Неизвестная роль не повод показывать пустую строку: имя значения честнее
        // и подсказывает, что API приехал с ролью, которой клиент ещё не знает.
        Assert.Equal("99", AccountText.RoleText((UserRole)99));

    [Fact]
    public void DisplayName_FirstAndLastName_JoinsWithSingleSpace() =>
        Assert.Equal("Ева Клиентова", AccountText.DisplayName(Profile()));

    [Fact]
    public void DisplayName_PaddedNames_TrimsEveryPart() =>
        // Пробелы по краям приходят из формы и не должны удваиваться между словами.
        Assert.Equal(
            "Ева Клиентова",
            AccountText.DisplayName(Profile(firstName: "  Ева ", lastName: " Клиентова  ")));

    [Fact]
    public void DisplayName_BlankNames_FallsBackToEmail() =>
        // Профиль без имени мог создать администратор: пустая шапка выглядела бы поломкой.
        Assert.Equal(
            "eva@example.com",
            AccountText.DisplayName(Profile(firstName: "  ", lastName: string.Empty)));

    [Fact]
    public void DisplayName_BlankNamesAndEmail_ShowsProfileFallback() =>
        Assert.Equal(
            "Профиль",
            AccountText.DisplayName(Profile(firstName: " ", lastName: " ", email: "  ")));

    [Fact]
    public void Initials_FirstAndLastName_ShowsTwoLetters() =>
        Assert.Equal("ЕК", AccountText.Initials(Profile()));

    [Fact]
    public void Initials_OnlyFirstName_ShowsOneLetter() =>
        Assert.Equal("Е", AccountText.Initials(Profile(lastName: string.Empty)));

    [Fact]
    public void Initials_BlankNames_ShowsFirstLetterOfEmail() =>
        // Email даёт одну букву: разбивать «eva@example.com» по словам значило бы взять «E» и «@».
        Assert.Equal(
            "E",
            AccountText.Initials(Profile(firstName: " ", lastName: string.Empty, email: "eva@example.com")));

    [Fact]
    public void Initials_NothingFilled_ShowsQuestionMark() =>
        // Аватар без данных — «?», а не пустой круг: место под буквы должно быть заполнено.
        Assert.Equal("?", AccountText.Initials(Profile(firstName: string.Empty, lastName: " ", email: " ")));

    [Fact]
    public void Moment_PlusThreeZone_ShowsWeekdayDayAndTime() =>
        Assert.Equal(ExpectedMoment, AccountText.Moment(Instant, PlusThreeHours));

    [Fact]
    public void MomentOrNever_NoLastLogin_ShowsNever() =>
        // null означает «входов ещё не было»: подставлять текущую дату или 1970 год нельзя.
        Assert.Equal(AccountText.Never, AccountText.MomentOrNever(null, PlusThreeHours));

    [Fact]
    public void MomentOrNever_LastLogin_FormatsMoment() =>
        Assert.Equal(ExpectedMoment, AccountText.MomentOrNever(Instant, PlusThreeHours));

    /// <summary>
    /// Профиль из базы; по умолчанию — клиент с именем, фамилией и аватаром.
    /// </summary>
    private static MyProfileResponse Profile(
        string firstName = "Ева",
        string lastName = "Клиентова",
        string email = "eva@example.com") =>
        new(
            Guid.Parse("0199d3b4-1f2c-7cc5-9c1a-2b3c4d5e6f70"),
            email,
            firstName,
            lastName,
            null,
            "https://cdn.example.test/avatar.jpg",
            UserRole.Customer,
            null,
            true,
            false,
            new DateTimeOffset(2026, 1, 10, 9, 0, 0, TimeSpan.Zero),
            null,
            false,
            PhotoModerationStatus.Approved,
            null);
}

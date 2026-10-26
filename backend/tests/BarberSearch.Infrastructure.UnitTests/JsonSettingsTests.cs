using System.Text.Json;
using System.Text.Json.Serialization;
using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Web.Common;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Проверяет настройки JSON MVC (R-23): перечисления приходят строками, целые числа отклоняются.
/// </summary>
/// <remarks>
/// <see cref="JsonStringEnumConverter"/> без аргументов принимает числа, поэтому регрессией
/// считается возврат parameterless-конвертера в <see cref="JsonSettings.Configure"/>: тест
/// <see cref="Configure_RejectsIntegerEnumValues"/> в этом случае падает.
/// </remarks>
public class JsonSettingsTests
{
    private static readonly JsonSerializerOptions PermissiveOptions = CreatePermissiveOptions();

    [Fact]
    public void Configure_DeserializesEnumNames()
    {
        var payload = JsonSerializer.Deserialize<CreateProfilePayload>(
            """{"languages":["Ru","En"],"paymentMethods":["Cash"]}""",
            CreateOptions());

        Assert.NotNull(payload);
        Assert.Equal([Language.Ru, Language.En], payload.Languages);
        Assert.Equal([PaymentMethod.Cash], payload.PaymentMethods);
    }

    [Fact]
    public void Configure_RejectsIntegerEnumValues()
    {
        var options = CreateOptions();

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CreateProfilePayload>(
            """{"languages":[999]}""",
            options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CreateProfilePayload>(
            """{"paymentMethods":[999]}""",
            options));
    }

    [Fact]
    public void ParameterlessConverter_AcceptsIntegerEnumValues()
    {
        // Показывает поведение конвертера по умолчанию, ради которого появился JsonSettings:
        // именно такой запрос проходил до R-23 и сохранялся в массив БД.
        var payload = JsonSerializer.Deserialize<CreateProfilePayload>(
            """{"paymentMethods":[999]}""",
            PermissiveOptions);

        Assert.NotNull(payload);
        Assert.Equal([(PaymentMethod)999], payload.PaymentMethods);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        // Те же web-умолчания (camelCase, регистронезависимость), что у JSON MVC.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        JsonSettings.Configure(options);

        return options;
    }

    private static JsonSerializerOptions CreatePermissiveOptions()
    {
        // Parameterless JsonStringEnumConverter принимает числа — ровно то поведение,
        // которое R-23 запретил в JsonSettings.Configure.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }

    private sealed record CreateProfilePayload(
        List<Language>? Languages,
        List<PaymentMethod>? PaymentMethods);
}

using BarberSearch.Mobile.Core.Configuration;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки адреса локального бэкенда: правило по устройству и явно заданный хост.
/// </summary>
/// <remarks>
/// Правило по устройству — не удобство, а необходимость: на реальном телефоне <c>10.0.2.2</c>
/// указывает на сам телефон, а не на машину разработчика, поэтому телефон ходит на
/// <c>localhost</c> через <c>adb reverse</c>. Явно заданный хост важнее правила: он закрывает
/// вариант «телефон в той же сети», где проброс портов не нужен. Строки сверяются дословно:
/// лишний или потерянный слэш меняет склейку адресов в <c>HttpClient</c>.
/// </remarks>
public sealed class DevelopmentEndpointsTests
{
    [Theory]
    [InlineData("192.168.1.10")]
    [InlineData("localhost")]
    [InlineData("dev.barbersearch.local")]
    [InlineData("fe80::1")]
    public void ResolveHost_ConfiguredHost_ReturnsItEvenOnEmulator(string host)
    {
        // Хост из сборки — это осознанный выбор разработчика, платформа его не переопределяет.
        Assert.Equal(host, DevelopmentEndpoints.ResolveHost(host, androidPlatform: true, virtualDevice: true));
    }

    [Fact]
    public void ResolveHost_ConfiguredHostWithSpaces_ReturnsTrimmedHost()
    {
        Assert.Equal("192.168.1.10", DevelopmentEndpoints.ResolveHost("  192.168.1.10  ", androidPlatform: true, virtualDevice: false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveHost_ConfiguredHostIsEmpty_ReturnsDeviceDefault(string? host)
    {
        // Пустое значение — это «не задано»: сборка без -p:DevApiHost должна подчиняться правилу,
        // а не падать и не уходить на пустой хост.
        Assert.Equal("10.0.2.2", DevelopmentEndpoints.ResolveHost(host, androidPlatform: true, virtualDevice: true));
    }

    [Theory]
    [InlineData("http://192.168.1.10")]
    [InlineData("192.168.1.10:5243")]
    [InlineData("192.168.1.10/api")]
    [InlineData("my host")]
    public void ResolveHost_ConfiguredHostIsNotAHost_ThrowsArgumentException(string host)
    {
        // Схема, порт или путь в хосте дали бы адрес вида http://http://… и молчаливую
        // неработающую сеть: опечатку в параметре сборки лучше показать сразу.
        Assert.Throws<ArgumentException>(() => DevelopmentEndpoints.ResolveHost(host, androidPlatform: true, virtualDevice: false));
    }

    [Fact]
    public void ResolveHost_AndroidEmulator_ReturnsHostAddressAlias()
    {
        // Эмулятор — виртуальная машина: 10.0.2.2 в ней — шлюз к хосту.
        Assert.Equal("10.0.2.2", DevelopmentEndpoints.ResolveHost(null, androidPlatform: true, virtualDevice: true));
    }

    [Fact]
    public void ResolveHost_AndroidDevice_ReturnsForwardedAddress()
    {
        // 10.0.2.2 на телефоне — сам телефон: без правила приложение искало бы API в себе.
        Assert.Equal("localhost", DevelopmentEndpoints.ResolveHost(null, androidPlatform: true, virtualDevice: false));
    }

    [Theory]
    [InlineData(true, "localhost")]
    [InlineData(false, "localhost")]
    public void ResolveHost_NonAndroidPlatform_ReturnsForwardedAddress(bool virtualDevice, string expected)
    {
        // Симулятор iOS разделяет сеть с Mac и видит localhost сам. Реальный iPhone видит машину
        // только по сети, и его адрес задаётся хостом сборки — правило здесь то же, что у Android.
        Assert.Equal(expected, DevelopmentEndpoints.ResolveHost(null, androidPlatform: false, virtualDevice: virtualDevice));
    }

    [Fact]
    public void ApiBaseAddress_Host_AddsSchemeApiPortAndTrailingSlash()
    {
        Assert.Equal("http://localhost:5243/", DevelopmentEndpoints.ApiBaseAddress("localhost"));
    }

    [Fact]
    public void SiteBaseAddress_Host_AddsSchemeSitePortAndTrailingSlash()
    {
        Assert.Equal("http://192.168.1.10:5173/", DevelopmentEndpoints.SiteBaseAddress("192.168.1.10"));
    }

    [Fact]
    public void ApiBaseAddress_Ipv6Host_BracketsIt()
    {
        // Без скобок двоеточия адреса слились бы с портом, и Uri не разобрал бы строку.
        Assert.Equal("http://[fe80::1]:5243/", DevelopmentEndpoints.ApiBaseAddress("fe80::1"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ApiBaseAddress_EmptyHost_ThrowsArgumentException(string host)
    {
        Assert.Throws<ArgumentException>(() => DevelopmentEndpoints.ApiBaseAddress(host));
    }
}

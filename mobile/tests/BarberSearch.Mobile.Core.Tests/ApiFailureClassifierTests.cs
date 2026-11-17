using System.Net;
using BarberSearch.Mobile.Core.Api;
using Xunit;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки классификации сбоев: что повторять, что показывать и что считать отменой.
/// </summary>
/// <remarks>
/// Исключение API собирается напрямую: конструктор внутренний, а тестовая сборка — друг
/// (см. <c>InternalsVisibleTo</c>). Ответ здесь не нужен, важен только код состояния.
/// </remarks>
public sealed class ApiFailureClassifierTests
{
    private static readonly Dictionary<string, string[]> NoValidationErrors = [];

    [Fact]
    public void Classify_HttpRequestException_ReturnsTransport()
    {
        var kind = ApiFailureClassifier.Classify(new HttpRequestException("Connection refused."));

        Assert.Equal(ApiFailureKind.Transport, kind);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(408)]
    [InlineData(429)]
    public void Classify_RetryableStatusCode_ReturnsRetryableServer(int statusCode)
    {
        var kind = ApiFailureClassifier.Classify(CreateApiException(statusCode));

        Assert.Equal(ApiFailureKind.RetryableServer, kind);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    public void Classify_TerminalStatusCode_ReturnsTerminal(int statusCode)
    {
        var kind = ApiFailureClassifier.Classify(CreateApiException(statusCode));

        Assert.Equal(ApiFailureKind.Terminal, kind);
    }

    [Fact]
    public void Classify_NullException_ReturnsNone()
    {
        Assert.Equal(ApiFailureKind.None, ApiFailureClassifier.Classify(null));
    }

    [Fact]
    public void Classify_CancelledOperation_ReturnsNone()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var kind = ApiFailureClassifier.Classify(
            new OperationCanceledException(),
            cancellation.Token);

        Assert.Equal(ApiFailureKind.None, kind);
    }

    [Fact]
    public void Classify_TaskCanceledWithLiveToken_ReturnsTransport()
    {
        var kind = ApiFailureClassifier.Classify(
            new TaskCanceledException(),
            CancellationToken.None);

        Assert.Equal(ApiFailureKind.Transport, kind);
    }

    [Fact]
    public void Classify_UnknownException_ReturnsTerminal()
    {
        var kind = ApiFailureClassifier.Classify(new InvalidOperationException("Unexpected."));

        Assert.Equal(ApiFailureKind.Terminal, kind);
    }

    [Theory]
    [InlineData(ApiFailureKind.None, false)]
    [InlineData(ApiFailureKind.Transport, true)]
    [InlineData(ApiFailureKind.RetryableServer, true)]
    [InlineData(ApiFailureKind.Terminal, false)]
    public void IsRetryable_EveryKind_ReturnsExpected(ApiFailureKind kind, bool expected)
    {
        Assert.Equal(expected, kind.IsRetryable());
    }

    private static BarberSearchApiException CreateApiException(int statusCode) =>
        new((HttpStatusCode)statusCode, "Request failed.", [], NoValidationErrors);
}

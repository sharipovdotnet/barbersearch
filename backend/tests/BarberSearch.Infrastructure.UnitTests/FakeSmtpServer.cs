using System.Net;
using System.Net.Sockets;
using System.Text;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Минимальный SMTP-сервер для тестов транспорта: принимает одно соединение и записывает
/// DATA, чтобы тест мог проверить собранное письмо, не ходя в настоящую почту.
/// </summary>
/// <remarks>
/// Поддерживается ровно тот диалог, который ведёт <c>SmtpClient</c> без TLS и аутентификации:
/// <c>EHLO</c> → <c>MAIL FROM</c> → <c>RCPT TO</c> → <c>DATA</c> → <c>QUIT</c>. Этого достаточно,
/// чтобы ветка успешной отправки проверялась детерминированно на <c>127.0.0.1</c>.
/// </remarks>
internal sealed class FakeSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource<string> _data =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _loop;

    private FakeSmtpServer(TcpListener listener)
    {
        _listener = listener;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _loop = Task.Run(RunAsync);
    }

    public int Port { get; }

    /// <summary>
    /// Тело DATA, полученное сервером; завершается после первого письма.
    /// </summary>
    public Task<string> DataAsync => _data.Task;

    public static FakeSmtpServer Start()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        return new FakeSmtpServer(listener);
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _listener.Stop();
        _listener.Dispose();

        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
        }

        _cts.Dispose();
    }

    private async Task RunAsync()
    {
        try
        {
            using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

            await writer.WriteAsync("220 localhost ESMTP\r\n");

            while (await reader.ReadLineAsync(_cts.Token) is { } line)
            {
                if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("HELO", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteAsync("250-localhost\r\n250 SIZE 10485760\r\n");
                }
                else if (line.Equals("DATA", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteAsync("354 End data with <CR><LF>.<CR><LF>\r\n");
                    _data.TrySetResult(await ReadDataAsync(reader));
                    await writer.WriteAsync("250 OK\r\n");
                }
                else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteAsync("221 Bye\r\n");

                    return;
                }
                else
                {
                    await writer.WriteAsync("250 OK\r\n");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task<string> ReadDataAsync(StreamReader reader)
    {
        var body = new StringBuilder();

        while (await reader.ReadLineAsync(_cts.Token) is { } line)
        {
            if (line == ".")
            {
                break;
            }

            body.AppendLine(line);
        }

        return body.ToString();
    }
}

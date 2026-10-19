using System.Globalization;
using System.Net;
using System.Text.Json;
using LoadHarness;

try
{
    var options = CliOptions.Parse(args);
    var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    var contextJson = await File.ReadAllTextAsync(options.ContextPath);
    var context = JsonSerializer.Deserialize<HarnessContext>(contextJson, jsonOptions)
                  ?? throw new InvalidOperationException(
                      $"Cannot deserialize harness context from '{options.ContextPath}'.");

    if (string.IsNullOrWhiteSpace(context.BaseUrl)
        || string.IsNullOrWhiteSpace(context.CustomerToken)
        || string.IsNullOrWhiteSpace(context.AdminToken))
    {
        throw new InvalidOperationException(
            "The context must contain baseUrl, customerToken and adminToken.");
    }

    context.Manifest.NormalizeIds();

    using var handler = new SocketsHttpHandler
    {
        MaxConnectionsPerServer = 512,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(5)
    };

    using var client = new HttpClient(handler)
    {
        BaseAddress = new Uri(context.BaseUrl.TrimEnd('/') + "/"),
        Timeout = TimeSpan.FromSeconds(30)
    };
    client.DefaultRequestHeaders.UserAgent.ParseAdd("BarberSearchLoadHarness/1.0");

    Console.WriteLine($"target: {context.BaseUrl}");
    Console.WriteLine($"scenario: {options.Scenario}, repeats: {options.Repeat}");
    Console.WriteLine($"data: search={context.Manifest.SearchDate}, slots={context.Manifest.SlotsDate}");
    Console.WriteLine();

    var runner = new Runner(context, options, client);
    var results = await runner.RunAsync();

    var fullOutPath = Path.GetFullPath(options.OutPath);
    Directory.CreateDirectory(Path.GetDirectoryName(fullOutPath)!);
    await File.WriteAllTextAsync(
        fullOutPath,
        JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));

    PrintSummary(results);
    Console.WriteLine();
    Console.WriteLine($"results: {fullOutPath}");

    if (!results.AllCorrect)
    {
        Console.WriteLine("correctness: FAILED");
        return 2;
    }

    Console.WriteLine("correctness: PASSED");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"load harness failed: {exception}");
    return 1;
}

static void PrintSummary(LoadResults results)
{
    Console.WriteLine();
    Console.WriteLine("=== measurements ===");
    Console.WriteLine(
        $"{"scenario",-22} {"run",3} {"count",7} {"req/s",9} {"p50",8} {"p95",8} {"p99",8} {"max",8} {"err",5} {"err%",6} {"check",6}");

    foreach (var scenario in results.Scenarios)
    {
        foreach (var run in scenario.Runs)
        {
            Console.WriteLine(
                $"{scenario.Name,-22} {run.Run,3} {run.Count,7} " +
                $"{run.Rps.ToString("F1", CultureInfo.InvariantCulture),9} " +
                $"{run.P50Ms.ToString("F1", CultureInfo.InvariantCulture),8} " +
                $"{run.P95Ms.ToString("F1", CultureInfo.InvariantCulture),8} " +
                $"{run.P99Ms.ToString("F1", CultureInfo.InvariantCulture),8} " +
                $"{run.MaxMs.ToString("F1", CultureInfo.InvariantCulture),8} " +
                $"{run.Errors,5} " +
                $"{run.ErrorRatePct.ToString("F2", CultureInfo.InvariantCulture),6} " +
                $"{(run.CorrectnessPassed == true ? "PASS" : run.CorrectnessPassed == false ? "FAIL" : "-"),6}");
        }

        var aggregate = scenario.Aggregate;
        Console.WriteLine(
            $"  {scenario.Name}: " +
            $"p95 min/med/max={Value(aggregate, "p95MinMs")}/{Value(aggregate, "p95MedianMs")}/{Value(aggregate, "p95MaxMs")} ms; " +
            $"p99 min/med/max={Value(aggregate, "p99MinMs")}/{Value(aggregate, "p99MedianMs")}/{Value(aggregate, "p99MaxMs")} ms; " +
            $"req/s med={Value(aggregate, "rpsMedian")}; " +
            $"budget={(scenario.BudgetPassed == true ? "PASS" : scenario.BudgetPassed == false ? "FAIL" : "n/a")} " +
            $"({scenario.BudgetDetail})");

        var failed = scenario.Runs.Where(run => run.CorrectnessPassed == false).ToArray();
        foreach (var run in failed)
        {
            Console.WriteLine($"  !! {scenario.Name} run {run.Run}: {run.CorrectnessDetail}");
        }
    }
}

static string Value(IReadOnlyDictionary<string, double> values, string key)
    => values.TryGetValue(key, out var value)
        ? value.ToString("F1", CultureInfo.InvariantCulture)
        : "n/a";

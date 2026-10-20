using System.Collections.Concurrent;
using System.Globalization;

namespace LoadHarness;

internal sealed class Stats
{
    private readonly object _gate = new();
    private readonly List<double> _latencies = new();

    public ConcurrentDictionary<int, int> Statuses { get; } = new();

    public void Add(int status, double latencyMs)
    {
        lock (_gate)
        {
            _latencies.Add(latencyMs);
        }

        Statuses.AddOrUpdate(status, 1, static (_, value) => value + 1);
    }

    public double[] Latencies()
    {
        lock (_gate)
        {
            return _latencies.ToArray();
        }
    }
}

internal static class StatsMath
{
    public static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.OrderBy(value => value).ToArray();
        var rank = (int)Math.Ceiling(percentile / 100.0 * sorted.Length) - 1;
        var index = Math.Clamp(rank, 0, sorted.Length - 1);

        return sorted[index];
    }

    public static double Median(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.OrderBy(value => value).ToArray();
        var middle = sorted.Length / 2;

        return sorted.Length % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2.0;
    }

    public static RunResult BuildRunResult(
        int run,
        IReadOnlyList<double> latencies,
        IReadOnlyDictionary<int, int> statuses,
        double elapsedSeconds,
        Func<int, bool> expectedStatus,
        bool? correctnessPassed = null,
        string? correctnessDetail = null)
    {
        var count = latencies.Count;
        var errors = statuses
            .Where(pair => !expectedStatus(pair.Key))
            .Sum(pair => pair.Value);

        var statusCounts = statuses
            .OrderBy(pair => pair.Key)
            .ToDictionary(
                pair => pair.Key.ToString(CultureInfo.InvariantCulture),
                pair => pair.Value);

        return new RunResult
        {
            Run = run,
            Count = count,
            Rps = elapsedSeconds > 0 ? count / elapsedSeconds : 0,
            P50Ms = Round(Percentile(latencies, 50)),
            P95Ms = Round(Percentile(latencies, 95)),
            P99Ms = Round(Percentile(latencies, 99)),
            MaxMs = Round(latencies.Count == 0 ? 0 : latencies.Max()),
            Errors = errors,
            ErrorRatePct = count > 0 ? Round(100.0 * errors / count) : 0,
            StatusCounts = statusCounts,
            CorrectnessPassed = correctnessPassed,
            CorrectnessDetail = correctnessDetail
        };
    }

    public static Dictionary<string, double> Aggregate(IReadOnlyList<RunResult> runs)
    {
        if (runs.Count == 0)
        {
            return new Dictionary<string, double>();
        }

        return new Dictionary<string, double>
        {
            ["p95MinMs"] = Round(runs.Min(run => run.P95Ms)),
            ["p95MedianMs"] = Round(Median(runs.Select(run => run.P95Ms).ToArray())),
            ["p95MaxMs"] = Round(runs.Max(run => run.P95Ms)),
            ["p99MinMs"] = Round(runs.Min(run => run.P99Ms)),
            ["p99MedianMs"] = Round(Median(runs.Select(run => run.P99Ms).ToArray())),
            ["p99MaxMs"] = Round(runs.Max(run => run.P99Ms)),
            ["rpsMin"] = Round(runs.Min(run => run.Rps)),
            ["rpsMedian"] = Round(Median(runs.Select(run => run.Rps).ToArray())),
            ["rpsMax"] = Round(runs.Max(run => run.Rps)),
            ["errorRateMaxPct"] = Round(runs.Max(run => run.ErrorRatePct))
        };
    }

    public static void ApplyBudget(
        ScenarioResult scenario,
        double? p95BudgetMs,
        double? p99BudgetMs)
    {
        scenario.BudgetP95Ms = p95BudgetMs;
        scenario.BudgetP99Ms = p99BudgetMs;

        if (scenario.Runs.Count == 0)
        {
            scenario.BudgetPassed = null;
            scenario.BudgetDetail = "no runs";
            return;
        }

        // Worst measured run, not the luckiest one.
        var worstP95 = scenario.Runs.Max(run => run.P95Ms);
        var worstP99 = scenario.Runs.Max(run => run.P99Ms);
        var p95Passed = p95BudgetMs is null || worstP95 < p95BudgetMs;
        var p99Passed = p99BudgetMs is null || worstP99 < p99BudgetMs;

        scenario.BudgetPassed = p95Passed && p99Passed;
        scenario.BudgetDetail =
            $"worst p95={worstP95.ToString("F1", CultureInfo.InvariantCulture)} ms " +
            $"(budget {p95BudgetMs?.ToString("F0", CultureInfo.InvariantCulture) ?? "n/a"}), " +
            $"worst p99={worstP99.ToString("F1", CultureInfo.InvariantCulture)} ms " +
            $"(budget {p99BudgetMs?.ToString("F0", CultureInfo.InvariantCulture) ?? "n/a"})";
    }

    public static double Round(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

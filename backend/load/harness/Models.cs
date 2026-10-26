using System.Text.Json.Serialization;

namespace LoadHarness;

internal sealed class HarnessContext
{
    public string BaseUrl { get; set; } = string.Empty;
    public string CustomerToken { get; set; } = string.Empty;
    public string AdminToken { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string AdminId { get; set; } = string.Empty;
    public Manifest Manifest { get; set; } = new();
}

internal sealed class Manifest
{
    public string GeneratedAt { get; set; } = string.Empty;
    public string SearchDate { get; set; } = string.Empty;
    public string SlotsDate { get; set; } = string.Empty;
    public List<string> Cities { get; set; } = new();
    public List<string> SpecializationIds { get; set; } = new();
    public List<string> ServiceIds { get; set; } = new();
    public List<BarberRef> Barbers { get; set; } = new();
    public List<ContentionReviewBarber> ContentionReviewBarbers { get; set; } = new();
    public List<CreateReviewBarber> CreateReviewBarbers { get; set; } = new();
    public ContendedBooking ContendedBooking { get; set; } = new();

    /// <summary>
    /// Normalizes every identifier to the canonical 8-4-4-4-12 GUID form.
    /// PostgreSQL's md5(...)::text is 32 hex characters, while System.Text.Json's
    /// Guid converter accepts only the hyphenated form.
    /// </summary>
    public void NormalizeIds()
    {
        for (var i = 0; i < SpecializationIds.Count; i++)
        {
            SpecializationIds[i] = GuidText.Normalize(SpecializationIds[i]);
        }

        for (var i = 0; i < ServiceIds.Count; i++)
        {
            ServiceIds[i] = GuidText.Normalize(ServiceIds[i]);
        }

        foreach (var barber in Barbers)
        {
            barber.Id = GuidText.Normalize(barber.Id);
            barber.HairServiceId = GuidText.Normalize(barber.HairServiceId);
            barber.SalonId = GuidText.Normalize(barber.SalonId);
        }

        foreach (var barber in ContentionReviewBarbers)
        {
            barber.BarberId = GuidText.Normalize(barber.BarberId);

            foreach (var review in barber.Reviews)
            {
                review.Id = GuidText.Normalize(review.Id);
            }

            for (var i = 0; i < barber.ExpectedReviewIds.Count; i++)
            {
                barber.ExpectedReviewIds[i] = GuidText.Normalize(barber.ExpectedReviewIds[i]);
            }
        }

        foreach (var barber in CreateReviewBarbers)
        {
            barber.BarberId = GuidText.Normalize(barber.BarberId);

            for (var i = 0; i < barber.BookingIds.Count; i++)
            {
                barber.BookingIds[i] = GuidText.Normalize(barber.BookingIds[i]);
            }
        }

        ContendedBooking.BarberId = GuidText.Normalize(ContendedBooking.BarberId);
        ContendedBooking.BarberServiceId = GuidText.Normalize(ContendedBooking.BarberServiceId);
        ContendedBooking.SalonId = GuidText.Normalize(ContendedBooking.SalonId);
    }
}

internal static class GuidText
{
    public static string Normalize(string value)
        => Guid.TryParse(value, out var guid) ? guid.ToString("D") : value;
}

internal sealed class BarberRef
{
    public string Id { get; set; } = string.Empty;
    public string HairServiceId { get; set; } = string.Empty;
    public string SalonId { get; set; } = string.Empty;
}

internal sealed class ContentionReviewBarber
{
    public string BarberId { get; set; } = string.Empty;
    public List<ReviewRef> Reviews { get; set; } = new();
    public List<string> ExpectedReviewIds { get; set; } = new();
    public int ExpectedCount { get; set; }
    public int ExpectedRatingSum { get; set; }
}

internal sealed class ReviewRef
{
    public string Id { get; set; } = string.Empty;
    public int Rating { get; set; }
    public bool InitiallyPublished { get; set; }
}

internal sealed class CreateReviewBarber
{
    public string BarberId { get; set; } = string.Empty;
    public List<string> BookingIds { get; set; } = new();
    public int BasePublishedCount { get; set; }
    public int BaseRatingSum { get; set; }
    public int NewRating { get; set; }
}

internal sealed class ContendedBooking
{
    public string BarberId { get; set; } = string.Empty;
    public string BarberServiceId { get; set; } = string.Empty;
    public string SalonId { get; set; } = string.Empty;
    public List<BookingSlot> Slots { get; set; } = new();
}

internal sealed class BookingSlot
{
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
}

internal sealed class LoadResults
{
    public string StartedAt { get; set; } = string.Empty;
    public string FinishedAt { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string Scenario { get; set; } = string.Empty;
    public int Repeat { get; set; }
    public Dictionary<string, string> Environment { get; set; } = new();
    public List<ScenarioResult> Scenarios { get; set; } = new();

    [JsonIgnore]
    public bool AllCorrect => Scenarios
        .SelectMany(scenario => scenario.Runs)
        .All(run => run.CorrectnessPassed != false);
}

internal sealed class ScenarioResult
{
    public string Name { get; set; } = string.Empty;
    public int Concurrency { get; set; }
    public double WarmupSeconds { get; set; }
    public double MeasureSeconds { get; set; }
    public int WarmupCount { get; set; }
    public double? BudgetP95Ms { get; set; }
    public double? BudgetP99Ms { get; set; }
    public bool? BudgetPassed { get; set; }
    public string? BudgetDetail { get; set; }
    public List<RunResult> Runs { get; set; } = new();
    public Dictionary<string, double> Aggregate { get; set; } = new();
}

internal sealed class RunResult
{
    public int Run { get; set; }
    public int Count { get; set; }
    public double Rps { get; set; }
    public double P50Ms { get; set; }
    public double P95Ms { get; set; }
    public double P99Ms { get; set; }
    public double MaxMs { get; set; }
    public int Errors { get; set; }
    public double ErrorRatePct { get; set; }
    public Dictionary<string, int> StatusCounts { get; set; } = new();
    public bool? CorrectnessPassed { get; set; }
    public string? CorrectnessDetail { get; set; }
}

internal sealed record CliOptions(
    string Scenario,
    int Repeat,
    string ContextPath,
    string OutPath,
    int SearchConcurrency,
    int SlotsConcurrency,
    int ReadSeconds)
{
    public static CliOptions Parse(string[] args)
    {
        var scenario = "all";
        var repeat = 3;
        var contextPath = "load/generated/context.json";
        var outPath = "load/generated/results.json";
        var searchConcurrency = 16;
        var slotsConcurrency = 16;
        var readSeconds = 15;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--scenario":
                    scenario = Next(args, ref i);
                    break;
                case "--repeat":
                    repeat = int.Parse(Next(args, ref i), System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--context":
                    contextPath = Next(args, ref i);
                    break;
                case "--out":
                    outPath = Next(args, ref i);
                    break;
                case "--search-concurrency":
                    searchConcurrency = int.Parse(Next(args, ref i), System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--slots-concurrency":
                    slotsConcurrency = int.Parse(Next(args, ref i), System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--read-seconds":
                    readSeconds = int.Parse(Next(args, ref i), System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--help":
                case "-h":
                    Console.WriteLine("""
                        LoadHarness options:
                          --scenario all|search|slots|booking|review
                          --repeat N
                          --context path/to/context.json
                          --out path/to/results.json
                          --search-concurrency N
                          --slots-concurrency N
                          --read-seconds N
                        """);
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'.");
            }
        }

        if (repeat < 1)
        {
            throw new ArgumentException("--repeat must be at least 1.");
        }

        return new CliOptions(
            scenario,
            repeat,
            contextPath,
            outPath,
            searchConcurrency,
            slotsConcurrency,
            readSeconds);
    }

    private static string Next(string[] args, ref int index)
    {
        index++;
        if (index >= args.Length)
        {
            throw new ArgumentException($"Missing value after '{args[index - 1]}'.");
        }

        return args[index];
    }
}

internal sealed record RequestPlan(
    HttpRequestMessage Request,
    Action<HttpResponseMessage, string>? OnResponse = null);

internal sealed record BatchItem(int Status, double LatencyMs, string Body);

internal sealed record BatchOutcome(
    IReadOnlyList<BatchItem> Items,
    bool Passed,
    string Detail,
    double ElapsedSeconds)
{
    /// <summary>
    /// Идентификаторы созданных строк (для review-create): их нужно опубликовать админом,
    /// прежде чем проверять рейтинг. Значение по умолчанию сохраняет прежние вызовы.
    /// </summary>
    public IReadOnlyList<string> CreatedIds { get; init; } = [];

    public BatchOutcome With(bool passed, string detail)
        => this with { Passed = passed, Detail = detail };
}

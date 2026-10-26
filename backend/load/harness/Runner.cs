using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace LoadHarness;

internal sealed class Runner
{
    private const int BookingContenders = 50;
    private const int SameKeyContenders = 10;
    private const int CreateReviewCount = 50;
    private const int CreateReviewWarmupCount = 10;
    private const int WarmupSeconds = 5;

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private readonly HarnessContext _context;
    private readonly CliOptions _options;
    private readonly HttpClient _client;
    private readonly ConcurrentDictionary<string, string> _searchCursors = new();

    public Runner(HarnessContext context, CliOptions options, HttpClient client)
    {
        _context = context;
        _options = options;
        _client = client;
    }

    public async Task<LoadResults> RunAsync()
    {
        var results = new LoadResults
        {
            StartedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            BaseUrl = _context.BaseUrl,
            Scenario = _options.Scenario,
            Repeat = _options.Repeat,
            Environment = new Dictionary<string, string>
            {
                ["framework"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                ["os"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                ["processorCount"] = Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture),
                ["serverGc"] = System.Runtime.GCSettings.IsServerGC.ToString(),
                ["manifestGeneratedAt"] = _context.Manifest.GeneratedAt
            }
        };

        try
        {
            if (Includes("search"))
            {
                results.Scenarios.Add(await RunReadScenarioAsync(
                    "search",
                    CreateSearchPlan,
                    _options.SearchConcurrency,
                    _options.ReadSeconds,
                    p95BudgetMs: 200,
                    p99BudgetMs: 500));
            }

            if (Includes("slots"))
            {
                results.Scenarios.Add(await RunReadScenarioAsync(
                    "slots",
                    CreateSlotsPlan,
                    _options.SlotsConcurrency,
                    _options.ReadSeconds,
                    p95BudgetMs: 300,
                    p99BudgetMs: 800));
            }

            if (Includes("booking"))
            {
                results.Scenarios.Add(await RunBookingAsync());
            }

            if (Includes("booking-idempotency"))
            {
                results.Scenarios.Add(await RunBookingIdempotencyAsync());
            }

            if (Includes("review"))
            {
                results.Scenarios.Add(await RunReviewPublicationAsync());
                results.Scenarios.Add(await RunCreateReviewAsync());
            }
        }
        finally
        {
            results.FinishedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        }

        return results;
    }

    private bool Includes(string scenario)
        => _options.Scenario == "all"
           || string.Equals(_options.Scenario, scenario, StringComparison.OrdinalIgnoreCase);

    private async Task<ScenarioResult> RunReadScenarioAsync(
        string name,
        Func<RequestPlan> requestFactory,
        int concurrency,
        int seconds,
        double? p95BudgetMs,
        double? p99BudgetMs)
    {
        Console.WriteLine($"{name}: warming up for {WarmupSeconds}s at concurrency {concurrency}...");

        var warmupStats = new Stats();
        await RunWorkersAsync(
            concurrency,
            TimeSpan.FromSeconds(WarmupSeconds),
            warmupStats,
            requestFactory,
            CancellationToken.None);

        var scenario = new ScenarioResult
        {
            Name = name,
            Concurrency = concurrency,
            WarmupSeconds = WarmupSeconds,
            MeasureSeconds = seconds,
            WarmupCount = warmupStats.Latencies().Length
        };

        Console.WriteLine($"{name}: warm-up sent {scenario.WarmupCount} requests; measuring {seconds}s x {_options.Repeat}...");

        for (var run = 1; run <= _options.Repeat; run++)
        {
            var stats = new Stats();
            var stopwatch = Stopwatch.StartNew();

            await RunWorkersAsync(
                concurrency,
                TimeSpan.FromSeconds(seconds),
                stats,
                requestFactory,
                CancellationToken.None);

            stopwatch.Stop();
            scenario.Runs.Add(StatsMath.BuildRunResult(
                run,
                stats.Latencies(),
                stats.Statuses,
                stopwatch.Elapsed.TotalSeconds,
                status => status == 200));

            Console.WriteLine(
                $"  {name} run {run}: {scenario.Runs[^1].Count} requests, " +
                $"{scenario.Runs[^1].Rps:F1} req/s, p95={scenario.Runs[^1].P95Ms:F1} ms, " +
                $"p99={scenario.Runs[^1].P99Ms:F1} ms");
        }

        scenario.Aggregate = StatsMath.Aggregate(scenario.Runs);
        StatsMath.ApplyBudget(scenario, p95BudgetMs, p99BudgetMs);

        return scenario;
    }

    private async Task RunWorkersAsync(
        int concurrency,
        TimeSpan duration,
        Stats stats,
        Func<RequestPlan> requestFactory,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(duration);

        var workers = Enumerable
            .Range(0, concurrency)
            .Select(_ => Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    RequestPlan plan;

                    try
                    {
                        plan = requestFactory();
                    }
                    catch (Exception) when (cts.IsCancellationRequested)
                    {
                        break;
                    }

                    using (plan.Request)
                    {
                        var started = Stopwatch.GetTimestamp();

                        try
                        {
                            using var response = await _client.SendAsync(
                                plan.Request,
                                HttpCompletionOption.ResponseContentRead,
                                cts.Token);

                            var body = await response.Content.ReadAsStringAsync(cts.Token);
                            plan.OnResponse?.Invoke(response, body);

                            stats.Add(
                                (int)response.StatusCode,
                                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                        }
                        catch (OperationCanceledException) when (cts.IsCancellationRequested)
                        {
                            break;
                        }
                        catch (Exception)
                        {
                            stats.Add(
                                0,
                                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                        }
                    }
                }
            }, CancellationToken.None))
            .ToArray();

        await Task.WhenAll(workers);
    }

    private RequestPlan CreateSearchPlan()
    {
        var manifest = _context.Manifest;
        var roll = Random.Shared.Next(100);

        string? city = null;
        string? specialization = null;
        string? service = null;
        string? date = null;
        string? cursor = null;
        var pageSize = 20;
        var captureCursor = false;

        if (roll < 25)
        {
            // No filters: the planner uses ix_barber_profiles_search_keyset directly (R-38).
        }
        else if (roll < 45)
        {
            city = Pick(manifest.Cities);
            captureCursor = true;
        }
        else if (roll < 60)
        {
            city = Pick(manifest.Cities);
            specialization = Pick(manifest.SpecializationIds);
            pageSize = 50;
        }
        else if (roll < 75)
        {
            city = Pick(manifest.Cities);
            service = Pick(manifest.ServiceIds);
        }
        else if (roll < 85)
        {
            date = manifest.SearchDate;
            pageSize = 100;
        }
        else if (roll < 95)
        {
            city = Pick(manifest.Cities);
            specialization = Pick(manifest.SpecializationIds);
            service = Pick(manifest.ServiceIds);
            date = manifest.SearchDate;
        }
        else
        {
            city = Pick(manifest.Cities);
            cursor = _searchCursors.GetValueOrDefault(city);

            if (cursor is null)
            {
                // No warmed-up cursor for this city: make the same city query and capture one.
                captureCursor = true;
            }
        }

        var query = new List<string>();

        if (city is not null)
        {
            query.Add("city=" + Uri.EscapeDataString(city));
        }

        if (specialization is not null)
        {
            query.Add("specializationId=" + specialization);
        }

        if (service is not null)
        {
            query.Add("hairServiceId=" + service);
        }

        if (date is not null)
        {
            query.Add("date=" + date);
        }

        if (cursor is not null)
        {
            query.Add("cursor=" + Uri.EscapeDataString(cursor));
        }

        query.Add("pageSize=" + pageSize.ToString(CultureInfo.InvariantCulture));

        var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/barbers?" + string.Join("&", query));
        request.Headers.Authorization = Bearer(_context.CustomerToken);

        Action<HttpResponseMessage, string>? onResponse = null;

        if (captureCursor && city is not null)
        {
            var capturedCity = city;
            onResponse = (response, body) => CaptureCursor(capturedCity, response, body);
        }

        return new RequestPlan(request, onResponse);
    }

    private RequestPlan CreateSlotsPlan()
    {
        var manifest = _context.Manifest;
        var barber = Pick(manifest.Barbers);
        var date = DateOnly.ParseExact(
            manifest.SlotsDate,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture);

        if (Random.Shared.Next(100) < 20)
        {
            date = date.AddDays(7);
        }

        var query = new List<string>
        {
            "date=" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };

        if (Random.Shared.Next(100) < 50)
        {
            query.Add("hairServiceId=" + barber.HairServiceId);
        }

        if (Random.Shared.Next(100) < 30)
        {
            query.Add("salonId=" + barber.SalonId);
        }

        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/barbers/{barber.Id}/slots?" + string.Join("&", query));
        request.Headers.Authorization = Bearer(_context.CustomerToken);

        return new RequestPlan(request);
    }

    private void CaptureCursor(string city, HttpResponseMessage response, string body)
    {
        if ((int)response.StatusCode != 200 || string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.TryGetProperty("nextCursor", out var cursor)
                && cursor.ValueKind == JsonValueKind.String)
            {
                var value = cursor.GetString();

                if (!string.IsNullOrWhiteSpace(value))
                {
                    _searchCursors[city] = value;
                }
            }
        }
        catch (JsonException)
        {
            // A malformed body is already counted as a non-200-less request by the caller;
            // cursor capture is best effort.
        }
    }

    private async Task<ScenarioResult> RunBookingAsync()
    {
        var contended = _context.Manifest.ContendedBooking;

        if (contended.Slots.Count < _options.Repeat + 1)
        {
            throw new InvalidOperationException(
                $"The manifest has {contended.Slots.Count} booking slots; " +
                $"{_options.Repeat + 1} are needed (1 warm-up + {_options.Repeat} measured).");
        }

        Console.WriteLine($"booking: warm-up batch of {BookingContenders} contenders...");

        var warmup = await RunBookingBatchAsync(contended.Slots[0], "warmup");
        if (!warmup.Passed)
        {
            throw new InvalidOperationException($"Booking warm-up failed: {warmup.Detail}");
        }

        var scenario = new ScenarioResult
        {
            Name = "booking-contended",
            Concurrency = BookingContenders,
            WarmupSeconds = 0,
            MeasureSeconds = 0,
            WarmupCount = warmup.Items.Count
        };

        for (var run = 1; run <= _options.Repeat; run++)
        {
            var outcome = await RunBookingBatchAsync(contended.Slots[run], $"r{run}");
            var stats = StatsFrom(outcome.Items);

            scenario.Runs.Add(StatsMath.BuildRunResult(
                run,
                stats.Latencies(),
                stats.Statuses,
                outcome.ElapsedSeconds,
                status => status is 201 or 409,
                outcome.Passed,
                outcome.Detail));

            Console.WriteLine(
                $"  booking run {run}: {outcome.Detail}; " +
                $"p95={scenario.Runs[^1].P95Ms:F1} ms");
        }

        scenario.Aggregate = StatsMath.Aggregate(scenario.Runs);
        StatsMath.ApplyBudget(scenario, p95BudgetMs: 300, p99BudgetMs: null);

        return scenario;
    }

    /// <summary>
    /// Same-key idempotency under contention, plus the two cases that the contended
    /// (different-key) batch cannot prove: a sequential repeat returns the same id,
    /// and a cancelled booking stops holding the slot.
    /// </summary>
    /// <remarks>
    /// Ten parallel POSTs share one idempotency key. Exactly one row may be inserted;
    /// the other nine must return the winner's id with <c>201</c>, never <c>409</c> or
    /// <c>500</c>. The SQL side (one row per key, one active row on the slot) is checked
    /// by <c>run.ps1</c>, which owns database access.
    /// </remarks>
    private async Task<ScenarioResult> RunBookingIdempotencyAsync()
    {
        var contended = _context.Manifest.ContendedBooking;

        // When the contended scenario also runs, it uses the first Repeat + 1 slots
        // (warm-up + measured). The idempotency runs must not collide with them.
        var firstSlot = Includes("booking") ? _options.Repeat + 1 : 0;
        var requiredSlots = firstSlot + _options.Repeat;

        if (contended.Slots.Count < requiredSlots)
        {
            throw new InvalidOperationException(
                $"The manifest has {contended.Slots.Count} booking slots; " +
                $"the idempotency scenario needs {requiredSlots} " +
                $"({firstSlot} reserved for booking-contended + {_options.Repeat} measured).");
        }

        var scenario = new ScenarioResult
        {
            Name = "booking-idempotency",
            Concurrency = SameKeyContenders,
            WarmupSeconds = 0,
            MeasureSeconds = 0
        };

        Console.WriteLine($"booking-idempotency: {SameKeyContenders} parallel same-key requests per run...");

        for (var run = 1; run <= _options.Repeat; run++)
        {
            var slot = contended.Slots[firstSlot + run - 1];
            var sameKey = $"load-idem-same-r{run}";
            var afterCancelKey = $"load-idem-after-cancel-r{run}";

            // Warm up the whole write path (EF model, JWT, service read, advisory lock,
            // insert, cancel) on the same slot. The row is cancelled, so the exclusion
            // constraint does not hold the slot for the measured batch.
            var warmupKey = $"load-idem-warmup-r{run}";
            var warmupBooking = await SendBookingAsync(slot, warmupKey);
            var warmupId = warmupBooking.Status == 201
                ? ParseCreatedId(warmupBooking.Body)
                : null;
            BatchItem? warmupCancellation = warmupId is null
                ? null
                : await SendAsync(BuildCancellationRequest(warmupId));

            var factories = Enumerable
                .Range(0, SameKeyContenders)
                .Select(_ => (Func<HttpRequestMessage>)(() => BuildBookingRequest(slot, sameKey)))
                .ToArray();

            var stopwatch = Stopwatch.StartNew();
            var batch = await RunBatchAsync(factories);
            stopwatch.Stop();

            var created = batch.Count(item => item.Status == 201);
            var other = batch.Count(item => item.Status != 201);
            var createdIds = batch
                .Where(item => item.Status == 201)
                .Select(item => ParseCreatedId(item.Body))
                .Where(id => id is not null)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var winnerId = createdIds.Length == 1 ? createdIds[0] : null;

            BatchItem? sequential = null;
            BatchItem? cancellation = null;
            BatchItem? afterCancel = null;
            var sequentialSameId = false;
            var afterCancelNewId = false;

            if (winnerId is not null)
            {
                // A repeat after the race has settled must hit the same row.
                sequential = await SendBookingAsync(slot, sameKey);
                sequentialSameId = sequential.Status == 201
                                   && string.Equals(
                                       ParseCreatedId(sequential.Body),
                                       winnerId,
                                       StringComparison.Ordinal);

                cancellation = await SendAsync(BuildCancellationRequest(winnerId));

                // A cancelled row (status 3) is outside the exclusion constraint, so the
                // same slot must accept a fresh booking with a different key.
                afterCancel = await SendBookingAsync(slot, afterCancelKey);
                var afterCancelId = afterCancel.Status == 201
                    ? ParseCreatedId(afterCancel.Body)
                    : null;
                afterCancelNewId = afterCancelId is not null
                                   && !string.Equals(afterCancelId, winnerId, StringComparison.Ordinal);
            }

            var passed = warmupBooking.Status == 201
                         && warmupCancellation is { Status: 204 }
                         && created == SameKeyContenders
                         && other == 0
                         && winnerId is not null
                         && sequential is { Status: 201 }
                         && sequentialSameId
                         && cancellation is { Status: 204 }
                         && afterCancel is { Status: 201 }
                         && afterCancelNewId;

            var detail =
                $"r{run}: warmup={warmupBooking.Status}/{StatusOf(warmupCancellation)}; " +
                $"batch statuses={FormatCounts(batch)}; distinctIds={createdIds.Length}; " +
                $"sequential={StatusOf(sequential)} sameId={sequentialSameId}; " +
                $"cancel={StatusOf(cancellation)}; " +
                $"afterCancel={StatusOf(afterCancel)} newId={afterCancelNewId}";

            var stats = StatsFrom(batch);
            scenario.Runs.Add(StatsMath.BuildRunResult(
                run,
                stats.Latencies(),
                stats.Statuses,
                stopwatch.Elapsed.TotalSeconds,
                status => status == 201,
                passed,
                detail));

            Console.WriteLine(
                $"  booking-idempotency run {run}: {detail}; " +
                $"p95={scenario.Runs[^1].P95Ms:F1} ms");
        }

        scenario.Aggregate = StatsMath.Aggregate(scenario.Runs);
        StatsMath.ApplyBudget(scenario, p95BudgetMs: null, p99BudgetMs: null);

        return scenario;
    }

    private async Task<BatchOutcome> RunBookingBatchAsync(BookingSlot slot, string tag)
    {
        var factories = Enumerable
            .Range(0, BookingContenders)
            .Select(index => (Func<HttpRequestMessage>)(() =>
                BuildBookingRequest(slot, $"load-contended-{tag}-{index}")))
            .ToArray();

        var stopwatch = Stopwatch.StartNew();
        var items = await RunBatchAsync(factories);
        stopwatch.Stop();

        var status201 = items.Count(item => item.Status == 201);
        var status409 = items.Count(item => item.Status == 409);
        var other = items.Count(item => item.Status is not 201 and not 409);
        var createdIds = items
            .Where(item => item.Status == 201)
            .Select(item => ParseCreatedId(item.Body))
            .Where(id => id is not null)
            .Distinct()
            .Count();
        // A loser is normally rejected by the exclusion constraint (23P01), but the
        // GiST check can also deadlock under 50-way contention (40P01). Both are a
        // lost contention and must be 409; neither is a server error.
        var conflictCodes = items
            .Where(item => item.Status == 409)
            .SelectMany(item => ParseErrorCodes(item.Body))
            .Count(code => code is "booking.add.overlap" or "booking.add.deadlock");

        var passed = status201 == 1
                     && status409 == BookingContenders - 1
                     && other == 0
                     && createdIds == 1
                     && conflictCodes == BookingContenders - 1;

        var detail =
            $"{tag}: statuses={FormatCounts(items)}; " +
            $"createdIds={createdIds}; conflictCodes={conflictCodes}; other={other}";

        return new BatchOutcome(items, passed, detail, stopwatch.Elapsed.TotalSeconds);
    }

    private HttpRequestMessage BuildBookingRequest(BookingSlot slot, string idempotencyKey)
    {
        var body = new
        {
            barberProfileId = _context.Manifest.ContendedBooking.BarberId,
            barberServiceId = _context.Manifest.ContendedBooking.BarberServiceId,
            salonId = _context.Manifest.ContendedBooking.SalonId,
            startAt = slot.StartAt,
            endAt = slot.EndAt,
            idempotencyKey,
            customerNote = (string?)null
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/bookings")
        {
            Content = JsonContent.Create(body, options: WebJson)
        };
        request.Headers.Authorization = Bearer(_context.CustomerToken);

        return request;
    }

    private HttpRequestMessage BuildCancellationRequest(string bookingId)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/bookings/{bookingId}/cancellation");
        request.Headers.Authorization = Bearer(_context.CustomerToken);

        return request;
    }

    private Task<BatchItem> SendBookingAsync(BookingSlot slot, string idempotencyKey)
        => SendAsync(BuildBookingRequest(slot, idempotencyKey));

    private async Task<BatchItem> SendAsync(HttpRequestMessage request)
    {
        using (request)
        {
            var started = Stopwatch.GetTimestamp();

            try
            {
                using var response = await _client.SendAsync(request).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                return new BatchItem(
                    (int)response.StatusCode,
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    body);
            }
            catch (Exception exception)
            {
                return new BatchItem(
                    0,
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    exception.Message);
            }
        }
    }

    private static string StatusOf(BatchItem? item)
        => item is null ? "not-run" : item.Status.ToString(CultureInfo.InvariantCulture);

    private async Task<ScenarioResult> RunReviewPublicationAsync()
    {
        var barbers = _context.Manifest.ContentionReviewBarbers;

        if (barbers.Count < _options.Repeat + 1)
        {
            throw new InvalidOperationException(
                $"The manifest needs {_options.Repeat + 1} contention-review barbers " +
                $"({_options.Repeat} measured + 1 warm-up); it has {barbers.Count}.");
        }

        var scenario = new ScenarioResult
        {
            Name = "review-publish-hide",
            Concurrency = barbers[0].Reviews.Count,
            WarmupSeconds = 0,
            MeasureSeconds = 0
        };

        Console.WriteLine("review-publish-hide: warm-up batch...");

        // The last barber is the warm-up one, so the measured runs use the first
        // Repeat entries and never collide with it.
        var warmupBarber = barbers[^1];
        var warmup = await RunReviewBatchAsync(warmupBarber, "warmup");
        var warmupVerification = await VerifyReviewStateAsync(
            warmupBarber.BarberId,
            warmupBarber.ExpectedCount,
            warmupBarber.ExpectedRatingSum,
            warmupBarber.ExpectedReviewIds);

        if (!warmup.Passed || !warmupVerification.Passed)
        {
            throw new InvalidOperationException(
                $"Review publication warm-up failed: {warmup.Detail} | {warmupVerification.Detail}");
        }

        scenario.WarmupCount = warmup.Items.Count;

        for (var run = 1; run <= _options.Repeat; run++)
        {
            var barber = barbers[run - 1];
            var outcome = await RunReviewBatchAsync(barber, $"r{run}");
            var verification = await VerifyReviewStateAsync(
                barber.BarberId,
                barber.ExpectedCount,
                barber.ExpectedRatingSum,
                barber.ExpectedReviewIds);

            var passed = outcome.Passed && verification.Passed;
            var detail = $"{outcome.Detail} | {verification.Detail}";
            var stats = StatsFrom(outcome.Items);

            scenario.Runs.Add(StatsMath.BuildRunResult(
                run,
                stats.Latencies(),
                stats.Statuses,
                outcome.ElapsedSeconds,
                status => status == 204,
                passed,
                detail));

            Console.WriteLine(
                $"  review-publish-hide run {run}: {detail}; " +
                $"p95={scenario.Runs[^1].P95Ms:F1} ms");
        }

        scenario.Aggregate = StatsMath.Aggregate(scenario.Runs);
        StatsMath.ApplyBudget(scenario, p95BudgetMs: 500, p99BudgetMs: 1000);

        return scenario;
    }

    private async Task<BatchOutcome> RunReviewBatchAsync(
        ContentionReviewBarber barber,
        string tag)
    {
        var factories = barber.Reviews
            .Select(review => (Func<HttpRequestMessage>)(() =>
            {
                var method = review.InitiallyPublished ? HttpMethod.Delete : HttpMethod.Post;
                var request = new HttpRequestMessage(
                    method,
                    $"/api/reviews/{review.Id}/publication");
                request.Headers.Authorization = Bearer(_context.AdminToken);

                return request;
            }))
            .ToArray();

        var stopwatch = Stopwatch.StartNew();
        var items = await RunBatchAsync(factories);
        stopwatch.Stop();

        var passed = items.All(item => item.Status == 204);
        var detail = $"{tag}: statuses={FormatCounts(items)}";

        return new BatchOutcome(items, passed, detail, stopwatch.Elapsed.TotalSeconds);
    }

    private async Task<ScenarioResult> RunCreateReviewAsync()
    {
        var barbers = _context.Manifest.CreateReviewBarbers;

        if (barbers.Count < _options.Repeat + 1)
        {
            throw new InvalidOperationException(
                $"The manifest needs {_options.Repeat + 1} create-review barbers " +
                $"({_options.Repeat} measured + 1 warm-up); it has {barbers.Count}.");
        }

        var scenario = new ScenarioResult
        {
            Name = "review-create",
            Concurrency = CreateReviewCount,
            WarmupSeconds = 0,
            MeasureSeconds = 0
        };

        Console.WriteLine("review-create: warm-up batch...");

        // Same split as publish/hide: last barber is warm-up, measured runs use the
        // first Repeat entries.
        var warmupBarber = barbers[^1];
        var warmup = await RunCreateReviewBatchAsync(
            warmupBarber,
            "warmup",
            CreateReviewWarmupCount);

        // R-35: creation leaves the review hidden (pre-moderation). The admin publishes the
        // created rows before the state check; publication latency is not part of the create stats.
        var warmupPublication = await PublishCreatedReviewsAsync(warmup.CreatedIds, "warmup");
        var warmupVerification = await VerifyReviewStateAsync(
            warmupBarber.BarberId,
            warmupBarber.BasePublishedCount + CreateReviewWarmupCount,
            warmupBarber.BaseRatingSum + warmupBarber.NewRating * CreateReviewWarmupCount);

        if (!warmup.Passed || !warmupPublication.Passed || !warmupVerification.Passed)
        {
            throw new InvalidOperationException(
                $"Create-review warm-up failed: {warmup.Detail} | " +
                $"{warmupPublication.Detail} | {warmupVerification.Detail}");
        }

        scenario.WarmupCount = warmup.Items.Count;

        for (var run = 1; run <= _options.Repeat; run++)
        {
            var barber = barbers[run - 1];
            var outcome = await RunCreateReviewBatchAsync(barber, $"r{run}", CreateReviewCount);
            var publication = await PublishCreatedReviewsAsync(outcome.CreatedIds, $"r{run}");
            var verification = await VerifyReviewStateAsync(
                barber.BarberId,
                barber.BasePublishedCount + CreateReviewCount,
                barber.BaseRatingSum + CreateReviewCount * barber.NewRating);

            var passed = outcome.Passed && publication.Passed && verification.Passed;
            var detail = $"{outcome.Detail} | {publication.Detail} | {verification.Detail}";
            var stats = StatsFrom(outcome.Items);

            scenario.Runs.Add(StatsMath.BuildRunResult(
                run,
                stats.Latencies(),
                stats.Statuses,
                outcome.ElapsedSeconds,
                status => status == 201,
                passed,
                detail));

            Console.WriteLine(
                $"  review-create run {run}: {detail}; " +
                $"p95={scenario.Runs[^1].P95Ms:F1} ms");
        }

        scenario.Aggregate = StatsMath.Aggregate(scenario.Runs);
        StatsMath.ApplyBudget(scenario, p95BudgetMs: 500, p99BudgetMs: 1000);

        return scenario;
    }

    private async Task<BatchOutcome> RunCreateReviewBatchAsync(
        CreateReviewBarber barber,
        string tag,
        int count)
    {
        var bookingIds = barber.BookingIds.Take(count).ToArray();

        if (bookingIds.Length != count)
        {
            throw new InvalidOperationException(
                $"Create-review barber {barber.BarberId} has only {barber.BookingIds.Count} bookings.");
        }

        var factories = bookingIds
            .Select((bookingId, index) => (Func<HttpRequestMessage>)(() =>
            {
                var body = new
                {
                    bookingId,
                    rating = barber.NewRating,
                    text = $"load-harness-{tag}-{index}"
                };

                var request = new HttpRequestMessage(HttpMethod.Post, "/api/reviews")
                {
                    Content = JsonContent.Create(body, options: WebJson)
                };
                request.Headers.Authorization = Bearer(_context.CustomerToken);

                return request;
            }))
            .ToArray();

        var stopwatch = Stopwatch.StartNew();
        var items = await RunBatchAsync(factories);
        stopwatch.Stop();

        var status201 = items.Count(item => item.Status == 201);
        var createdIds = items
            .Where(item => item.Status == 201)
            .Select(item => ParseCreatedId(item.Body))
            .Where(id => id is not null)
            .Distinct(StringComparer.Ordinal)
            .Select(id => id!)
            .ToArray();
        var passed = status201 == count && createdIds.Length == count;
        var detail =
            $"{tag}: statuses={FormatCounts(items)}; createdIds={createdIds.Length}";

        return new BatchOutcome(items, passed, detail, stopwatch.Elapsed.TotalSeconds)
        {
            CreatedIds = createdIds
        };
    }

    /// <summary>
    /// Publishes the reviews created by the measured create batches, as the admin.
    /// </summary>
    /// <remarks>
    /// Since R-35 a created review is hidden until an administrator publishes it, so the
    /// create scenario verifies the rating recalculation on this publication step. The
    /// requests are sent as one batch after the create batch and are deliberately kept out of
    /// the create latency statistics: the scenario still measures <c>POST api/reviews</c>.
    /// Any status other than <c>204</c> fails the run.
    /// </remarks>
    private async Task<BatchOutcome> PublishCreatedReviewsAsync(
        IReadOnlyList<string> reviewIds,
        string tag)
    {
        if (reviewIds.Count == 0)
        {
            return new BatchOutcome(
                [],
                false,
                $"{tag}: no created reviews to publish",
                0);
        }

        var factories = reviewIds
            .Select(reviewId => (Func<HttpRequestMessage>)(() =>
            {
                var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"/api/reviews/{reviewId}/publication");
                request.Headers.Authorization = Bearer(_context.AdminToken);

                return request;
            }))
            .ToArray();

        var stopwatch = Stopwatch.StartNew();
        var items = await RunBatchAsync(factories);
        stopwatch.Stop();

        var passed = items.All(item => item.Status == 204);
        var detail = $"{tag}: publication statuses={FormatCounts(items)}";

        return new BatchOutcome(items, passed, detail, stopwatch.Elapsed.TotalSeconds);
    }

    private async Task<(bool Passed, string Detail)> VerifyReviewStateAsync(
        string barberId,
        int expectedCount,
        int expectedRatingSum,
        IReadOnlyCollection<string>? expectedReviewIds = null)
    {
        using var card = await GetJsonAsync($"/api/barbers/{barberId}", _context.CustomerToken);
        using var reviews = await GetJsonAsync(
            $"/api/reviews?barberProfileId={barberId}&pageSize=100",
            _context.CustomerToken);

        var cardRoot = card.RootElement;
        var cardCount = cardRoot.GetProperty("reviewsCount").GetInt32();
        decimal? cardRating = null;

        if (cardRoot.TryGetProperty("rating", out var ratingElement)
            && ratingElement.ValueKind != JsonValueKind.Null)
        {
            cardRating = ratingElement.GetDecimal();
        }

        var items = reviews.RootElement.GetProperty("items").EnumerateArray().ToList();
        var actualCount = items.Count;
        var actualSum = items.Sum(item => item.GetProperty("rating").GetInt32());
        var expectedAverage = expectedCount == 0
            ? 0m
            : decimal.Round(
                (decimal)expectedRatingSum / expectedCount,
                2,
                MidpointRounding.ToEven);
        var actualAverage = actualCount == 0
            ? 0m
            : decimal.Round(
                (decimal)actualSum / actualCount,
                2,
                MidpointRounding.ToEven);

        var idsMatch = true;

        if (expectedReviewIds is not null)
        {
            var expected = expectedReviewIds
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            var actual = items
                .Select(item => item.GetProperty("id").GetString() ?? string.Empty)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();

            idsMatch = expected.SequenceEqual(actual, StringComparer.Ordinal);
        }

        var passed = actualCount == expectedCount
                     && actualSum == expectedRatingSum
                     && cardCount == expectedCount
                     && cardRating == expectedAverage
                     && actualAverage == expectedAverage
                     && idsMatch;

        var detail =
            $"expected count={expectedCount} sum={expectedRatingSum} avg={expectedAverage.ToString(CultureInfo.InvariantCulture)}; " +
            $"actual list count={actualCount} sum={actualSum} avg={actualAverage.ToString(CultureInfo.InvariantCulture)}; " +
            $"card count={cardCount} rating={(cardRating?.ToString(CultureInfo.InvariantCulture) ?? "null")}; " +
            $"idsMatch={idsMatch}";

        return (passed, detail);
    }

    private async Task<JsonDocument> GetJsonAsync(string url, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = Bearer(token);

        using var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"GET {url} returned {(int)response.StatusCode}: {body}");
        }

        return JsonDocument.Parse(body);
    }

    private async Task<IReadOnlyList<BatchItem>> RunBatchAsync(
        IReadOnlyList<Func<HttpRequestMessage>> factories)
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = factories
            .Select(async factory =>
            {
                await gate.Task.ConfigureAwait(false);

                using var request = factory();
                var started = Stopwatch.GetTimestamp();

                try
                {
                    using var response = await _client.SendAsync(request).ConfigureAwait(false);
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    return new BatchItem(
                        (int)response.StatusCode,
                        Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                        body);
                }
                catch (Exception exception)
                {
                    return new BatchItem(
                        0,
                        Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                        exception.Message);
                }
            })
            .ToArray();

        gate.SetResult(true);

        return await Task.WhenAll(tasks);
    }

    private static Stats StatsFrom(IReadOnlyList<BatchItem> items)
    {
        var stats = new Stats();

        foreach (var item in items)
        {
            stats.Add(item.Status, item.LatencyMs);
        }

        return stats;
    }

    private static string FormatCounts(IEnumerable<BatchItem> items)
        => string.Join(
            ", ",
            items
                .GroupBy(item => item.Status)
                .OrderBy(group => group.Key)
                .Select(group => $"{group.Key}:{group.Count()}"));

    private static string? ParseCreatedId(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            return document.RootElement.TryGetProperty("id", out var id)
                ? id.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string[] ParseErrorCodes(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.TryGetProperty("errorCodes", out var codes)
                && codes.ValueKind == JsonValueKind.Array)
            {
                return codes
                    .EnumerateArray()
                    .Where(code => code.ValueKind == JsonValueKind.String)
                    .Select(code => code.GetString()!)
                    .ToArray();
            }
        }
        catch (JsonException)
        {
            // Fall back to the status code; the correctness check reports the missing code.
        }

        return Array.Empty<string>();
    }

    private static T Pick<T>(IReadOnlyList<T> values)
    {
        if (values.Count == 0)
        {
            throw new InvalidOperationException("Cannot pick from an empty manifest list.");
        }

        return values[Random.Shared.Next(values.Count)];
    }

    private static AuthenticationHeaderValue Bearer(string token)
        => new("Bearer", token);
}

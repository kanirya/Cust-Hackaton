using System.Diagnostics.Metrics;

namespace TaxNetGuardian.Api;

/// <summary>
/// Observability metrics (Phase 8 / §21.2). Uses the OpenTelemetry-compatible
/// <see cref="System.Diagnostics.Metrics.Meter"/> API so a production OpenTelemetry collector
/// can scrape it, plus lightweight in-process counters that the /metrics endpoint renders in
/// Prometheus text format and /api/system/metrics renders as JSON.
/// </summary>
public static class TaxNetMetrics
{
    public const string MeterName = "TaxNetGuardian";
    public static readonly Meter Meter = new(MeterName, "1.0.0");

    private static readonly Counter<long> RequestCounter = Meter.CreateCounter<long>("taxnet_api_request_count");
    private static readonly Counter<long> ErrorCounter = Meter.CreateCounter<long>("taxnet_api_error_count");
    private static readonly Histogram<double> RequestLatency = Meter.CreateHistogram<double>("taxnet_api_request_latency_ms");

    private static long _requestCount;
    private static long _errorCount;
    private static long _latencySumMs;
    private static long _latencyCount;

    public static void RecordRequest(int statusCode, double elapsedMs)
    {
        Interlocked.Increment(ref _requestCount);
        Interlocked.Add(ref _latencySumMs, (long)elapsedMs);
        Interlocked.Increment(ref _latencyCount);
        RequestCounter.Add(1);
        RequestLatency.Record(elapsedMs);
        if (statusCode >= 500)
        {
            Interlocked.Increment(ref _errorCount);
            ErrorCounter.Add(1);
        }
    }

    public static ApiMetricsSnapshot Snapshot()
    {
        var requests = Interlocked.Read(ref _requestCount);
        var errors = Interlocked.Read(ref _errorCount);
        var latencyCount = Interlocked.Read(ref _latencyCount);
        var latencySum = Interlocked.Read(ref _latencySumMs);
        return new ApiMetricsSnapshot(
            requests,
            errors,
            requests == 0 ? 0 : Math.Round((double)errors / requests, 4),
            latencyCount == 0 ? 0 : Math.Round((double)latencySum / latencyCount, 2));
    }
}

public sealed record ApiMetricsSnapshot(long RequestCount, long ErrorCount, double ErrorRate, double AvgLatencyMs);

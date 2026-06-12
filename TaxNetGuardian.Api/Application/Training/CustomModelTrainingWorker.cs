namespace TaxNetGuardian.Api;

/// <summary>
/// Background worker that continuously distils the local model from accumulated frontier-LLM
/// examples. It auto-triggers a training run whenever enough new teacher examples have been
/// collected since the last run, throttled by a minimum interval. Manual training via the API
/// runs through the same TaxNetState.TrainCustomModel path, so the two never conflict (the state
/// guards against concurrent runs).
/// </summary>
public sealed class CustomModelTrainingWorker : BackgroundService
{
    private readonly TaxNetState _state;
    private readonly ILogger<CustomModelTrainingWorker> _logger;

    // Auto-train once this many new examples accumulate, but no more often than the interval.
    private const int NewExampleTrigger = 12;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MinTrainInterval = TimeSpan.FromMinutes(2);

    private DateTimeOffset _lastTrainedAt = DateTimeOffset.MinValue;

    public CustomModelTrainingWorker(TaxNetState state, ILogger<CustomModelTrainingWorker> logger)
    {
        _state = state;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Rehydrate any previously-trained model from the loaded snapshot, then bootstrap-train if
        // we already have teacher examples but no active model yet.
        _state.RehydrateCustomModel();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, stoppingToken);

                if (_state.IsTrainingInProgress)
                {
                    continue;
                }

                var pending = _state.PendingTrainingExamples;
                var staleEnough = DateTimeOffset.UtcNow - _lastTrainedAt >= MinTrainInterval;
                var bootstrap = !_state.CustomModelReady && pending >= 3;

                if ((pending >= NewExampleTrigger && staleEnough) || bootstrap)
                {
                    _logger.LogInformation(
                        "Auto-training custom model: {Pending} new examples (bootstrap={Bootstrap}).",
                        pending, bootstrap);
                    var run = _state.TrainCustomModel("auto-training-worker");
                    _lastTrainedAt = DateTimeOffset.UtcNow;
                    _logger.LogInformation(
                        "Custom model run {RunId} {Status}: v{Version}, validationSimilarity={Sim:P1}.",
                        run.Id, run.Status, run.Version, run.Metrics?.ValidationSimilarity ?? 0);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Custom model training worker iteration failed.");
            }
        }
    }
}

using Microsoft.Extensions.Hosting;

namespace TaxNetGuardian.Api;

public sealed class PostgresProjectionStartupService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<PostgresProjectionStartupService> _logger;
    private readonly TaxNetPlatformOptions _options;

    public PostgresProjectionStartupService(
        IServiceProvider services,
        ILogger<PostgresProjectionStartupService> logger,
        TaxNetPlatformOptions options)
    {
        _services = services;
        _logger = logger;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Storage.OperationalStore.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            using var scope = _services.CreateScope();
            var state = scope.ServiceProvider.GetRequiredService<TaxNetState>();
            var postgres = scope.ServiceProvider.GetRequiredService<PostgresOperationalSchemaService>();

            var migration = await postgres.EnsureSchemaAsync(stoppingToken);
            if (!migration.Reachable)
            {
                _logger.LogWarning("PostgreSQL startup migration did not complete: {Error}", migration.Error);
                return;
            }

            var sync = await postgres.SyncFromStateAsync(state, stoppingToken);
            if (!sync.Reachable)
            {
                _logger.LogWarning("PostgreSQL startup projection sync did not complete: {Error}", sync.Error);
                return;
            }

            _logger.LogInformation("PostgreSQL startup projection synced {RowsProjected} rows.", sync.RowsProjected);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PostgreSQL startup projection sync failed.");
        }
    }
}

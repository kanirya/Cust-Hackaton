using Serilog;
using Serilog.Formatting.Compact;

namespace TaxNetGuardian.Worker.Shared;

public static class WorkerLogging
{
    public static ILogger CreateLogger(string workerName)
    {
        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .Enrich.WithProperty("WorkerName", workerName)
            .WriteTo.Console(new CompactJsonFormatter())
            .CreateLogger();
    }
}

using Headless.Jobs.Base;

namespace Headless.Jobs.Dashboard.Jwt.Demo;

/// <summary>
/// Demo job functions — some intentionally fail to populate the dashboard with
/// failed jobs and exception stack traces.
/// </summary>
public sealed class DemoJobs
{
    [JobFunction("Demo_OrderProcessing")]
    public async Task OrderProcessingAsync(JobFunctionContext context, CancellationToken cancellationToken)
    {
        await Task.Delay(Random.Shared.Next(50, 200), cancellationToken);
    }

    [JobFunction("Demo_DataSync")]
    public async Task DataSyncAsync(JobFunctionContext context, CancellationToken cancellationToken)
    {
        // ~25% failure rate — simulates sync timeout
        if (Random.Shared.Next(4) == 0)
        {
            throw new TimeoutException(
                $"Data sync timed out for job {context.Id}. "
                    + "The upstream data warehouse at https://warehouse.internal/api/sync "
                    + "did not respond within the configured 30-second timeout."
            );
        }

        await Task.Delay(Random.Shared.Next(100, 400), cancellationToken);
    }

    [JobFunction("Demo_ReportGeneration")]
    public async Task ReportGenerationAsync(JobFunctionContext context, CancellationToken cancellationToken)
    {
        await Task.Delay(Random.Shared.Next(100, 500), cancellationToken);
    }

    [JobFunction("Demo_PaymentReconciliation")]
    public async Task PaymentReconciliationAsync(JobFunctionContext context, CancellationToken cancellationToken)
    {
        // ~20% failure rate — simulates reconciliation issues
        if (Random.Shared.Next(5) == 0)
        {
            throw new AggregateException(
                "Payment reconciliation failed",
                new InvalidOperationException(
                    "Ledger entry mismatch: expected $142.50 USD but settlement reported $141.08 USD. "
                        + "This may indicate a currency conversion rounding error."
                ),
                new TimeoutException(
                    "Accounting service at https://accounting.internal/api/reconcile "
                        + "did not respond within the configured 15-second timeout."
                )
            );
        }

        await Task.Delay(Random.Shared.Next(80, 300), cancellationToken);
    }

    [JobFunction("Demo_CleanupExpiredSessions")]
    public async Task CleanupExpiredSessionsAsync(JobFunctionContext context, CancellationToken cancellationToken)
    {
        await Task.Delay(Random.Shared.Next(30, 100), cancellationToken);
    }

    [JobFunction("Demo_HealthCheck")]
    public async Task HealthCheckAsync(JobFunctionContext context, CancellationToken cancellationToken)
    {
        // ~10% failure rate
        if (Random.Shared.Next(10) == 0)
        {
            throw new InvalidOperationException(
                "Health check failed: Redis at redis.internal:6379 returned LOADING state. "
                    + "The instance may be recovering from a snapshot restore."
            );
        }

        await Task.Delay(Random.Shared.Next(10, 50), cancellationToken);
    }
}

namespace Headless.Jobs.Dashboard.Jwt.Demo;

// Demo jobs. Some intentionally fail to populate the dashboard with failed jobs and exception stack traces.

[Job(DemoJobIdentities.OrderProcessing)]
public sealed class OrderProcessingJob : IJob
{
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        await Task.Delay(Random.Shared.Next(50, 200), cancellationToken);
    }
}

[Job(DemoJobIdentities.DataSync)]
public sealed class DataSyncJob : IJob
{
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken)
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
}

[Job(DemoJobIdentities.ReportGeneration)]
public sealed class ReportGenerationJob : IJob
{
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        await Task.Delay(Random.Shared.Next(100, 500), cancellationToken);
    }
}

[Job(DemoJobIdentities.PaymentReconciliation)]
public sealed class PaymentReconciliationJob : IJob
{
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken)
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
}

[Job(DemoJobIdentities.CleanupExpiredSessions)]
public sealed class CleanupExpiredSessionsJob : IJob
{
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        await Task.Delay(Random.Shared.Next(30, 100), cancellationToken);
    }
}

[Job(DemoJobIdentities.HealthCheck)]
public sealed class HealthCheckJob : IJob
{
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken)
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

/// <summary>The demo's job identities, shared with the seeder that writes rows for them directly.</summary>
public static class DemoJobIdentities
{
    public const string OrderProcessing = "demo.order-processing";
    public const string DataSync = "demo.data-sync";
    public const string ReportGeneration = "demo.report-generation";
    public const string PaymentReconciliation = "demo.payment-reconciliation";
    public const string CleanupExpiredSessions = "demo.cleanup-expired-sessions";
    public const string HealthCheck = "demo.health-check";
}

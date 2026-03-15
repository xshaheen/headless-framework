# Headless.Jobs.SourceGenerator

C# source generator for Jobs that generates boilerplate code for background job registration and execution.

## Problem Solved

Eliminates reflection overhead and manual job registration by generating compile-time code for Jobs job functions marked with `[Jobs]` attribute.

## Key Features

- **Zero Reflection**: Compile-time code generation
- **Auto-Registration**: Automatic job discovery and registration
- **Type Safety**: Compile-time validation of job signatures
- **DI Integration**: Generates constructor injection code
- **Incremental**: Fast rebuild with incremental generation
- **Diagnostics**: Rich compile-time error messages

## Installation

```bash
dotnet add package Headless.Jobs.SourceGenerator
```

## Quick Start

```csharp
// Define job with attribute
[Jobs("*/5 * * * *")] // Every 5 minutes
public static class CleanupJob
{
    public static async Task ExecuteAsync(IServiceProvider sp, CancellationToken ct)
    {
        var logger = sp.GetRequiredService<ILogger<CleanupJob>>();
        logger.LogInformation("Running cleanup");
    }
}

// Source generator creates:
// - Job registration code
// - Execution delegates
// - Constructor injection
// - Request type mapping

// No manual registration needed - jobs auto-discovered at compile time
```

## Configuration

No runtime configuration. Uses attributes:

```csharp
// Cron job
[Jobs("0 0 * * *", Priority = TickerTaskPriority.High)]
public static class DailyReport { /* ... */ }

// Job with request payload
[Jobs("ProcessOrder")]
public sealed class OrderProcessor(IOrderService orders)
{
    public async Task ExecuteAsync(
        TickerFunctionContext<OrderRequest> context,
        CancellationToken ct)
    {
        await orders.ProcessAsync(context.Request, ct);
    }
}

// Custom constructor
public sealed class ComplexJob
{
    [JobsConstructor]
    public ComplexJob(ILogger<ComplexJob> logger, IConfiguration config)
    {
        // Custom initialization
    }

    [Jobs("ComplexTask")]
    public async Task ExecuteAsync(/* ... */) { }
}
```

## Dependencies

- `Microsoft.CodeAnalysis.CSharp` (analyzer/generator)

## Side Effects

Generates `JobsInstanceFactoryExtensions.g.cs` at compile time with:
- Module initializer for auto-registration
- Job execution delegates
- Constructor factory methods
- Request type registrations

# Headless.Jobs.SourceGenerator

Roslyn incremental source generator that eliminates reflection and manual job registration for the Jobs scheduler.

## Problem Solved

Without the source generator, every job class or method must be manually registered with the Jobs runtime at startup, and job dispatch uses reflection to invoke methods. The source generator scans for `[JobFunction]` attributes at compile time and emits a module initializer that auto-registers all discovered jobs before `Main` runs, with zero reflection at runtime.

## Key Features

- **Zero reflection**: all dispatch delegates are generated as strongly-typed lambdas.
- **Auto-registration**: a `[ModuleInitializer]` in the generated file (`JobsInstanceFactory.g.cs`) registers job delegates before any host startup code runs.
- **Type safety**: compile-time validation of job method signatures and cron expression syntax.
- **DI constructor injection**: generates constructor factory methods; uses `[JobsConstructor]` constructor when present, otherwise the first public constructor.
- **Incremental**: only re-generates when marked methods change (fast on large solutions).
- **Rich diagnostics**: compile-time errors for unknown function names, ambiguous constructors, invalid cron expressions, and mismatched context types.

## Installation

```bash
dotnet add package Headless.Jobs.SourceGenerator
```

## Quick Start

```csharp
using Headless.Jobs.Base;
using Headless.Jobs.Enums;

// Static cron job (no DI)
[JobFunction("Cleanup", cronExpression: "*/5 * * * *")]
public static async Task ExecuteAsync(IServiceProvider sp, CancellationToken ct)
{
    sp.GetRequiredService<ILogger<Program>>().LogInformation("Cleaning up");
    await Task.CompletedTask;
}

// Instance job with primary constructor DI
[JobFunction("ProcessOrder")]
public sealed class OrderProcessor(IOrderService orders)
{
    public async Task ExecuteAsync(JobFunctionContext<OrderRequest> context, CancellationToken ct)
        => await orders.ProcessAsync(context.Request, ct);
}

// Multiple constructors — mark the target with [JobsConstructor]
public sealed class ComplexJob
{
    [JobsConstructor]
    public ComplexJob(ILogger<ComplexJob> logger, IConfiguration config) { }

    public ComplexJob() { } // ignored by generator

    [JobFunction("ComplexTask")]
    public async Task ExecuteAsync(CancellationToken ct) { }
}

// High-priority cron
[JobFunction("DailyReport", cronExpression: "0 0 * * *", taskPriority: JobPriority.High)]
public static Task ExecuteAsync(IServiceProvider sp, CancellationToken ct) => Task.CompletedTask;
```

## Configuration

No runtime configuration. Attributes are the sole interface. Generated output file: `JobsInstanceFactory.g.cs` (a `[ModuleInitializer]` in the consuming assembly).

## Dependencies

- `Microsoft.CodeAnalysis.CSharp` (build-time Roslyn API; not a runtime dependency)

## Side Effects

Emits `JobsInstanceFactory.g.cs` at compile time. The generated file:
- Contains a `[ModuleInitializer]` that registers job delegates and request-type mappings with the Jobs runtime.
- Contains constructor factory lambdas for each discovered job class.
- Has no effect at runtime beyond the one-time module initializer invocation.

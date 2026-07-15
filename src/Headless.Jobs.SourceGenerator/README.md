# Headless.Jobs.SourceGenerator

Roslyn incremental source generator that eliminates reflection and manual job registration for the Jobs scheduler.

## Problem Solved

Without the source generator, every job class or method must be manually registered with the Jobs runtime at startup, and job dispatch uses reflection to invoke methods. The source generator scans for `[JobFunction]` attributes at compile time and emits a module initializer that auto-registers all discovered jobs before `Main` runs, with zero reflection at runtime.

## Key Features

- **Zero reflection**: all dispatch delegates are generated as strongly-typed lambdas.
- **Auto-registration**: a `[ModuleInitializer]` in the generated file (`JobsInstanceFactory.g.cs`) registers job delegates before any host startup code runs.
- **Descriptor indexes**: generates delegate-free `JobFunctionDescriptor` values for every typed and requestless function; `JobFunctionProvider` exposes frozen indexes by durable name and, for typed functions, request `Type`.
- **Type safety**: compile-time validation of job method signatures and cron expression syntax.
- **DI constructor injection**: generates constructor factory methods; uses `[JobsConstructor]` constructor when present, otherwise the first public constructor.
- **Incremental**: only re-generates when marked methods change (fast on large solutions).
- **Collision safety**: HF005 rejects duplicate function names and HF011 rejects duplicate typed request mappings within a compilation. Runtime provider construction reports cross-assembly conflicts in deterministic ordinal order.
- **Rich diagnostics**: compile-time errors for unknown function names, ambiguous constructors, invalid cron expressions, mismatched context types, and ambiguous scheduling identities.

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

Attributes are the sole authoring interface; there is no runtime middleware discovery configuration. Middleware implementations must still be registered with DI because generated dispatch resolves them from the bounded scheduling or execution scope. Generated output file: `JobsInstanceFactory.g.cs` (a `[ModuleInitializer]` in the consuming assembly).

`[JobFunction]` remains the only handler discovery model. Requestless functions have a descriptor whose `RequestType` is `null`; typed functions are indexed by both their durable function name and the exact request `Type`. Priority and maximum concurrency come from the attribute and remain descriptor metadata rather than per-schedule options.

Stage-specific `JobScheduleMiddleware<TMiddleware>` and `JobExecuteMiddleware<TMiddleware>` declarations are discovered at compile time and produce direct schedule/execute calls in the declaring assembly. Assembly declarations are global unless their `Function` property explicitly targets descriptor metadata from a referenced assembly. A declaration placed beside a local `[JobFunction]` derives that function identity without duplicating a string. Both placements normalize to the same generated registration model; ordering is priority then stable middleware type identity. Generated descriptor metadata makes external identities available without runtime scanning.

## Dependencies

- `Microsoft.CodeAnalysis.CSharp` (build-time Roslyn API; not a runtime dependency)

## Side Effects

Emits `JobsInstanceFactory.g.cs` at compile time. The generated file:
- Contains a `[ModuleInitializer]` that registers job delegates, request-type mappings, and delegate-free descriptors with the Jobs runtime.
- Contains constructor factory lambdas for each discovered job class.
- Has no effect at runtime beyond the one-time module initializer invocation.

// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Running;
using Headless.EntityFramework.Benchmarks;
using Headless.EntityFramework.Benchmarks.ContextCost;
using Headless.EntityFramework.Benchmarks.SaveCost;

if (args.Contains("--save-pipeline-probe", StringComparer.Ordinal))
{
    await SavePipelineProbe.RunAsync().ConfigureAwait(false);
    return;
}

if (args.Contains("--save-cost-probe", StringComparer.Ordinal))
{
    await SaveCostProbe.RunAsync(args).ConfigureAwait(false);
    return;
}

if (args.Contains("--stamp-check", StringComparer.Ordinal))
{
    await StampCheck.RunAsync().ConfigureAwait(false);
    return;
}

if (args.Contains("--audit-row-cost", StringComparer.Ordinal))
{
    await AuditRowCost.RunAsync().ConfigureAwait(false);
    return;
}

if (args.Contains("--context-probe", StringComparer.Ordinal))
{
    await ContextCostProbe.RunAsync().ConfigureAwait(false);
    return;
}

BenchmarkSwitcher
    .FromTypes([typeof(SavePipelineBenchmarks), typeof(AuditedInsertBenchmarks)])
    .Run(args, BenchmarkRunConfig.Create());

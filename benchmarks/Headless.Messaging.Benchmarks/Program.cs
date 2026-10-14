// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Running;
using Headless.Messaging.Benchmarks;
using Headless.Messaging.Benchmarks.Scenarios;

if (args is ["relay"])
{
    await PostgreSqlRelayScenario.RunAsync();

    return;
}

if (args is ["send-window"])
{
    await ParallelSendWindowScenario.RunAsync();

    return;
}

BenchmarkSwitcher
    .FromTypes([
        typeof(ConsumeDispatchBenchmarks),
        typeof(PublishDispatchBenchmarks),
        typeof(MessageHeaderBenchmarks),
        typeof(DelayedSchedulingBenchmarks),
        typeof(RetryEngineBenchmarks),
        typeof(ContentRefreshBenchmarks),
    ])
    .Run(args, BenchmarkRunConfig.Create());

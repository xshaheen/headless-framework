// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Running;
using Headless.Messaging.Benchmarks;
using Headless.Messaging.Benchmarks.Scenarios;

BenchmarkSwitcher
    .FromTypes([typeof(ConsumeDispatchBenchmarks), typeof(MessageHeaderBenchmarks)])
    .Run(args, BenchmarkRunConfig.Create());

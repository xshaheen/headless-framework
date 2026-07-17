// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Running;
using Headless.Jobs.Benchmarks;

BenchmarkSwitcher
    .FromTypes([typeof(JobsRequestSerializationBenchmarks), typeof(JobsExecutionFanoutBenchmarks)])
    .Run(args, BenchmarkRunConfig.Create(args));

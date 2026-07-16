// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Running;
using Headless.Api.Idempotency.Benchmarks;

BenchmarkSwitcher.FromTypes([typeof(RequestBufferingBenchmarks)]).Run(args);

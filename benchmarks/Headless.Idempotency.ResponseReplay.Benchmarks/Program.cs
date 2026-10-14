// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Running;
using Headless.Idempotency.ResponseReplay.Benchmarks;

BenchmarkSwitcher.FromTypes([typeof(RequestBufferingBenchmarks), typeof(ResponseSnapshotBenchmarks)]).Run(args);

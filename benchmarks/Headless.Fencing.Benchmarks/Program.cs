// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Running;
using Headless.Fencing.Benchmarks;

BenchmarkSwitcher.FromTypes([typeof(StatusReadBenchmarks)]).Run(args, BenchmarkRunConfig.Create(args));

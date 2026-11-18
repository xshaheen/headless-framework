// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Running;
using Headless.Checks.Benchmarks;

BenchmarkSwitcher.FromTypes([typeof(PassingCheckBenchmarks)]).Run(args, BenchmarkRunConfig.Create(args));

// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Running;
using Headless.Sequences.Benchmarks;

BenchmarkSwitcher.FromTypes([typeof(NextBenchmarks)]).Run(args, BenchmarkRunConfig.Create(args));

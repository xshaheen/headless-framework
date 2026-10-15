// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Running;
using Headless.Threading.Benchmarks;

BenchmarkSwitcher.FromTypes([typeof(KeyedAsyncLockBenchmarks)]).Run(args, BenchmarkRunConfig.Create(args));

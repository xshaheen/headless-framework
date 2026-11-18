// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Running;
using Headless.Sql.Benchmarks;

BenchmarkSwitcher.FromTypes([typeof(BinaryParameterBenchmarks)]).Run(args, BenchmarkRunConfig.Create(args));

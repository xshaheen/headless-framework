// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Running;
using Headless.Logging.Benchmarks;

BenchmarkSwitcher.FromTypes([typeof(RedactingLoggerBenchmarks)]).Run(args, BenchmarkRunConfig.Create(args));

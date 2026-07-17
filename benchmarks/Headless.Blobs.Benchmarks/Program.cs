// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Running;
using Headless.Blobs.Benchmarks;

BenchmarkSwitcher.FromTypes([typeof(BlobJsonBenchmarks)]).Run(args, BenchmarkRunConfig.Create(args));

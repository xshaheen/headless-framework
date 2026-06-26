// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Running;
using Headless.Serializer.Benchmarks;

BenchmarkSwitcher
    .FromTypes([typeof(SerializeBenchmarks), typeof(DeserializeBenchmarks)])
    .Run(args, BenchmarkRunConfig.Create(args));

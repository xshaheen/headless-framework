// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Running;
using Headless.Encryption.Benchmarks;

BenchmarkSwitcher.FromTypes([typeof(EncryptedColumnBenchmarks)]).Run(args, BenchmarkRunConfig.Create(args));

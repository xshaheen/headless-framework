// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Running;
using Headless.Api.Identity.Benchmarks;

BenchmarkSwitcher.FromTypes([typeof(CredentialAuthenticationBenchmarks)]).Run(args);

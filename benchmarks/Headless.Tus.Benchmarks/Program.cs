// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Running;
using Headless.Tus.Benchmarks;

if (args is ["requests"])
{
    await StorageRequestScenario.RunAsync();

    return;
}

BenchmarkSwitcher.FromTypes([typeof(TusS3UploadBenchmarks)]).Run(args, BenchmarkRunConfig.Create(args));

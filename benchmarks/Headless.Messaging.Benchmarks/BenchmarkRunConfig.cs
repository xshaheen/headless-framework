// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;

namespace Headless.Messaging.Benchmarks;

internal static class BenchmarkRunConfig
{
    private static readonly string _ArtifactsPath = Path.Combine("artifacts", "benchmark", "messaging");

    public static IConfig Create(string[] args)
    {
        // DefaultConfig.Instance already registers the GitHub markdown + HTML exporters; re-adding them triggers
        // a "exporter already present" config warning and duplicate output, so only the diagnoser is added here.
        return ManualConfig
            .Create(DefaultConfig.Instance)
            .WithArtifactsPath(_ArtifactsPath)
            .AddDiagnoser(MemoryDiagnoser.Default);
    }
}

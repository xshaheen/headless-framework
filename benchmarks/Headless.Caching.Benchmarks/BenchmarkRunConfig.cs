// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;

namespace Headless.Caching.Benchmarks;

internal static class BenchmarkRunConfig
{
    private static readonly string s_artifactsPath = Path.Combine("artifacts", "BenchmarkDotNet.Artifacts");

    public static IConfig Create()
    {
        return ManualConfig
            .Create(DefaultConfig.Instance)
            .WithArtifactsPath(s_artifactsPath)
            .AddJob(Job.Default.WithId("cache-comparison"))
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddExporter(MarkdownExporter.GitHub, HtmlExporter.Default);
    }
}

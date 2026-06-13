// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;

namespace Headless.Caching.Benchmarks;

internal static class BenchmarkRunConfig
{
    private static readonly string s_artifactsPath = Path.Combine("artifacts", "benchmark", "caching");

    public static IConfig Create(string[] args)
    {
        var config = ManualConfig
            .Create(DefaultConfig.Instance)
            .WithArtifactsPath(s_artifactsPath)
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddExporter(MarkdownExporter.GitHub, HtmlExporter.Default);

        if (!_HasExplicitJob(args))
        {
            config.AddJob(Job.Default.WithId("cache-comparison"));
        }

        return config;
    }

    private static bool _HasExplicitJob(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg is "-j" or "--job")
            {
                return true;
            }

            if (arg.StartsWith("--job=", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

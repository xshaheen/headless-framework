// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Couchbase.Linq;
using Headless.Api.Security.Jwt;
using Headless.AuditLog;
using Headless.Caching;
using Headless.Couchbase.Context;
using Headless.Domain;
using Headless.Features.Models;
using Headless.Jobs;
using Headless.Jobs.SourceGenerator;
using Headless.Messaging.Dashboard.NodeDiscovery;
using Headless.Payments.Paymob.CashIn.Models.Payment;
using Headless.Payments.Paymob.CashOut;
using Headless.Redis;
using Headless.Security;
using Headless.Settings.Models;
using Headless.Sitemaps;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using StackExchange.Redis;

namespace Tests;

internal static class ConsumerCompilation
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> _References = new(_CreateReferences);

    public static ImmutableArray<Diagnostic> Compile(string source)
    {
        return _CreateCompilation("External.Consumer.Contracts", source).GetDiagnostics();
    }

    public static INamedTypeSymbol GetTypeSymbol(string metadataName)
    {
        return _CreateCompilation("External.Consumer.Metadata", string.Empty).GetTypeByMetadataName(metadataName)
            ?? throw new InvalidOperationException($"Public type '{metadataName}' was not found.");
    }

    public static (ImmutableArray<Diagnostic> Diagnostics, GeneratorDriverRunResult RunResult) CompileWithJobsGenerator(
        string source
    )
    {
        var compilation = _CreateCompilation("External.Consumer.Jobs", source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new JobsIncrementalSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics
        );

        return (outputCompilation.GetDiagnostics().AddRange(generatorDiagnostics), driver.GetRunResult());
    }

    private static CSharpCompilation _CreateCompilation(string assemblyName, string source)
    {
        return CSharpCompilation.Create(
            assemblyName,
            [
                CSharpSyntaxTree.ParseText(
                    source,
                    CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
                    $"{assemblyName}.cs"
                ),
            ],
            _References.Value,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                generalDiagnosticOption: ReportDiagnostic.Error,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );
    }

    private static ImmutableArray<MetadataReference> _CreateReferences()
    {
        var trustedPlatformAssemblies =
            ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries
            ) ?? [];

        var contractAssemblies = new[]
        {
            typeof(AuditLogWriteRequest).Assembly,
            typeof(SettingDefinitionCreateOptions).Assembly,
            typeof(FeatureDefinitionCreateOptions).Assembly,
            typeof(JwtTokenValidationRequest).Assembly,
            typeof(SitemapUrl).Assembly,
            typeof(CashInBillingData).Assembly,
            typeof(IFactoryCacheStore).Assembly,
            typeof(INodeDiscoveryProvider).Assembly,
            typeof(IPaymobCashOutBroker).Assembly,
            typeof(DocumentSetExtensions).Assembly,
            typeof(ConnectionMultiplexerExtensions).Assembly,
            typeof(IStringHashService).Assembly,
            typeof(HeadlessGeometryExtensions).Assembly,
            typeof(IEntity).Assembly,
            typeof(JobFunctionDelegate).Assembly,
            typeof(JobsIncrementalSourceGenerator).Assembly,
            typeof(Microsoft.EntityFrameworkCore.QueryableExtensions).Assembly,
            typeof(DictionaryExtensions).Assembly,
            typeof(IDocumentSet<>).Assembly,
            typeof(IConnectionMultiplexer).Assembly,
            typeof(GeometryFactory).Assembly,
            typeof(DbContext).Assembly,
            typeof(IServiceCollection).Assembly,
        };

        return
        [
            .. trustedPlatformAssemblies
                .Concat(contractAssemblies.Select(assembly => assembly.Location))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)),
        ];
    }
}

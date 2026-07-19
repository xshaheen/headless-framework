// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Testing.Tests;
using Microsoft.CodeAnalysis;

namespace Tests;

public sealed class ConsumerContractCompileTests : TestBase
{
    [Fact]
    public void external_consumer_should_compile_against_hardened_public_contracts()
    {
        var diagnostics = ConsumerCompilation.Compile(
            """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading;
            using System.Threading.Tasks;
            using Couchbase.Linq;
            using Headless.Api.Security.Jwt;
            using Headless.AuditLog;
            using Headless.Caching;
            using Headless.Couchbase.Context;
            using Headless.Domain;
            using Headless.Features.Models;
            using Headless.Jobs;
            using Headless.Messaging.Dashboard.NodeDiscovery;
            using Headless.Payments.Paymob.CashIn.Models.Payment;
            using Headless.Payments.Paymob.CashOut;
            using Headless.Payments.Paymob.CashOut.Models;
            using Headless.Redis;
            using Headless.Security;
            using Headless.Settings.Models;
            using Headless.Sitemaps;
            using Microsoft.EntityFrameworkCore;
            using NetTopologySuite.Geometries;
            using StackExchange.Redis;

            namespace External.Consumer;

            public sealed class ConsumerEntity : IEntity<string>
            {
                public string Id => "consumer";
                public IReadOnlyList<object> GetKeys() => [Id];
            }

            public sealed class ConsumerIntEntity : IEntity<int>
            {
                public int Id => 42;
                public IReadOnlyList<object> GetKeys() => [Id];
            }

            public static class ContractUsage
            {
                public static void ConstructRequestsAndOptions()
                {
                    _ = new AuditLogWriteRequest
                    {
                        Action = "consumer.created",
                        EntityType = "Consumer",
                        EntityId = null,
                    };
                    _ = new AuditLogQuery { Action = null, Limit = 20 };
                    _ = new SettingDefinitionCreateOptions { Name = "Consumer.Theme", DefaultValue = null };
                    _ = new FeatureDefinitionCreateOptions { Name = "Consumer.Enabled", Description = null };
                    _ = new JwtTokenValidationRequest
                    {
                        Token = "token",
                        SigningKey = "signing-key",
                        Issuer = "issuer",
                        Audience = "audience",
                        EncryptingKey = null,
                    };
                    _ = new SitemapUrl(
                        new Uri("https://example.test"),
                        new SitemapUrlOptions { Images = null, WriteAlternateLanguageCodes = null }
                    );
                    _ = new SitemapUrl(new Uri("https://example.test/defaults"));
                    _ = new SitemapUrl(Array.Empty<SitemapAlternateUrl>());
                    _ = new CashInBillingData("Ada", "Lovelace", "+201000000000", "ada@example.test");
                }

                public static void UseSecurity(
                    IStringHashService hashService,
                    IStringEncryptionService encryptionService,
                    StringHashOptions hashOptions,
                    StringEncryptionOptions encryptionOptions
                )
                {
                    _ = hashService.Create("value");
                    _ = encryptionService.Encrypt("value");
                    _ = hashOptions;
                    _ = encryptionOptions;
                }

                public static Coordinate[] UseGeometryExtensions(GeometryFactory factory)
                {
                    var point = factory.CreatePoint(1, 2);
                    _ = HeadlessGeometryExtensions.ToCoordinates([point]);
                    return new[] { point }.ToCoordinates();
                }

                public static void UseGenericContracts(
                    IDocumentSet<ConsumerEntity> set,
                    IDocumentSet<ConsumerIntEntity> intSet,
                    IQueryable<ConsumerEntity> query,
                    IQueryable<ConsumerIntEntity> intQuery,
                    IDictionary<string, string> first,
                    IDictionary<string, string> second,
                    IDictionary<int, string> intFirst,
                    IDictionary<int, string> intSecond
                )
                {
                    _ = DocumentSetExtensions.GetAsync<ConsumerEntity, string>(set, "consumer", CancellationToken.None);
                    _ = DocumentSetExtensions.GetAllReplicasAsync<ConsumerEntity, string>(set, "consumer");
                    _ = DocumentSetExtensions.GetAsync<ConsumerIntEntity, int>(intSet, 42, CancellationToken.None);
                    _ = Microsoft.EntityFrameworkCore.HeadlessQueryableExtensions.FirstByIdAsync<ConsumerEntity, string>(
                        query,
                        "consumer",
                        CancellationToken.None
                    );
                    _ = Microsoft.EntityFrameworkCore.HeadlessQueryableExtensions.FirstByIdAsync<ConsumerIntEntity, int>(
                        intQuery,
                        42,
                        CancellationToken.None
                    );
                    _ = first.DictionaryEqual(second);
                    _ = intFirst.DictionaryEqual(intSecond);
                }

                public static void UseNormalizedAsyncContracts(
                    IFactoryCacheStore cache,
                    INodeDiscoveryProvider discovery,
                    IPaymobCashOutBroker cashOut,
                    CashOutDisburseRequest request,
                    IConnectionMultiplexer redis,
                    CancellationToken cancellationToken
                )
                {
                    _ = cache.TryGetEntryAsync<string>(
                        "consumer",
                        FactoryCacheReadOptions.None,
                        cancellationToken
                    );
                    _ = cache.TryGetAllEntriesAsync<string>(
                        ["consumer"],
                        FactoryCacheReadOptions.None,
                        cancellationToken
                    );
                    _ = cache.TryGetEntryAsync<string>("consumer");
                    _ = cache.TryGetAllEntriesAsync<string>(["consumer"]);
                    _ = discovery.GetNodesAsync(cancellationToken: cancellationToken);
                    _ = discovery.GetNodeAsync("node", cancellationToken: cancellationToken);
                    _ = discovery.RegisterNodeAsync(cancellationToken);
                    _ = discovery.GetNamespacesAsync(cancellationToken);
                    _ = discovery.ListServicesAsync(cancellationToken: cancellationToken);
                    _ = cashOut.DisburseAsync(request, cancellationToken);
                    _ = redis.CountAllKeysAsync(cancellationToken);

                    JobFunctionDelegate handler = static (_, _, _) => Task.CompletedTask;
                    _ = handler;
                }
            }
            """
        );

        diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should()
            .BeEmpty(_FormatDiagnostics(diagnostics));
    }

    [Fact]
    public void nullable_couchbase_document_id_should_be_rejected()
    {
        var diagnostics = ConsumerCompilation.Compile(
            """
            using System.Collections.Generic;
            using Couchbase.Linq;
            using Headless.Couchbase.Context;
            using Headless.Domain;

            public sealed class ConsumerEntity : IEntity<string>
            {
                public string Id => "consumer";
                public IReadOnlyList<object> GetKeys() => [Id];
            }

            public static class ContractUsage
            {
                public static void Use(IDocumentSet<ConsumerEntity> set, string? id)
                    => _ = DocumentSetExtensions.GetAsync<ConsumerEntity, string?>(set, id);
            }
            """
        );

        _AssertOnlyExpectedError(diagnostics, "CS8714", "DocumentSetExtensions.GetAsync");
    }

    [Fact]
    public void nullable_dictionary_key_should_be_rejected()
    {
        var diagnostics = ConsumerCompilation.Compile(
            """
            using System.Collections.Generic;

            public static class ContractUsage
            {
                public static bool Use()
                    => DictionaryExtensions.DictionaryEqual<string?, string>(null, null);
            }
            """
        );

        _AssertOnlyExpectedError(diagnostics, "CS8714", "DictionaryExtensions.DictionaryEqual");
    }

    [Fact]
    public void non_equatable_entity_key_should_be_rejected()
    {
        var diagnostics = ConsumerCompilation.Compile(
            """
            using System.Collections.Generic;
            using System.Linq;
            using Headless.Domain;
            using Microsoft.EntityFrameworkCore;

            public sealed class NonEquatableKey;

            public sealed class ConsumerEntity : IEntity<NonEquatableKey>
            {
                public NonEquatableKey Id { get; } = new();
                public IReadOnlyList<object> GetKeys() => [Id];
            }

            public static class ContractUsage
            {
                public static void Use(IQueryable<ConsumerEntity> query, NonEquatableKey id)
                    => _ = Microsoft.EntityFrameworkCore.HeadlessQueryableExtensions.FirstByIdAsync<ConsumerEntity, NonEquatableKey>(
                        query,
                        id
                    );
            }
            """
        );

        _AssertOnlyExpectedError(diagnostics, "CS0311", "HeadlessQueryableExtensions.FirstByIdAsync");
    }

    [Fact]
    public void every_couchbase_document_id_type_parameter_should_be_non_nullable()
    {
        var documentSetExtensions = ConsumerCompilation.GetTypeSymbol(
            "Headless.Couchbase.Context.DocumentSetExtensions"
        );
        var documentIdParameters = documentSetExtensions
            .GetMembers()
            .OfType<IMethodSymbol>()
            .Where(method => method.DeclaredAccessibility == Accessibility.Public)
            .SelectMany(method => method.TypeParameters)
            .Where(parameter => string.Equals(parameter.Name, "TId", StringComparison.Ordinal))
            .ToArray();

        documentIdParameters.Should().NotBeEmpty();
        documentIdParameters.Should().OnlyContain(parameter => parameter.HasNotNullConstraint);
    }

    [Fact]
    public async Task jobs_generator_should_emit_delegate_for_external_consumer_assembly()
    {
        var (diagnostics, runResult) = ConsumerCompilation.CompileWithJobsGenerator(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Headless.Jobs.Base;

            namespace External.Consumer;

            public sealed class ConsumerJobs
            {
                [JobFunction("consumer.contract")]
                public Task RunAsync(JobFunctionContext context, CancellationToken cancellationToken)
                    => Task.CompletedTask;
            }
            """
        );

        diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should()
            .BeEmpty(_FormatDiagnostics(diagnostics));
        var generatedSources = await Task.WhenAll(
            runResult.GeneratedTrees.Select(async tree => (await tree.GetTextAsync(AbortToken)).ToString())
        );

        generatedSources.Should().Contain(source => source.Contains("\"consumer.contract\"", StringComparison.Ordinal));
    }

    private static void _AssertOnlyExpectedError(
        ImmutableArray<Diagnostic> diagnostics,
        string expectedId,
        string expectedMethod
    )
    {
        var errors = diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();

        errors.Should().ContainSingle();
        errors[0].Id.Should().Be(expectedId);
        errors[0].GetMessage(CultureInfo.InvariantCulture).Should().Contain(expectedMethod);
    }

    private static string _FormatDiagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        return string.Join(Environment.NewLine, diagnostics);
    }
}

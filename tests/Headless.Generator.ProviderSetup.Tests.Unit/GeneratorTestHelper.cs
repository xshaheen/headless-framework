// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Generator.ProviderSetup;
using Headless.Http.Effects;
using Headless.Sms;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;

namespace Tests;

internal static class GeneratorTestHelper
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> _References = new(() =>
        GeneratorCompilation.LoadedAssemblyReferences(
            typeof(OutboundEffect).Assembly,
            typeof(Headless.Checks.Argument).Assembly,
            typeof(Microsoft.Extensions.Configuration.IConfiguration).Assembly,
            typeof(Microsoft.Extensions.Configuration.BinderOptions).Assembly,
            typeof(HeadlessSmsSetupBuilder).Assembly,
            typeof(ISmsSender).Assembly,
            typeof(HeadlessOptionsServiceCollectionExtensions).Assembly,
            typeof(AbstractValidator<>).Assembly,
            typeof(HttpStandardResilienceOptions).Assembly,
            typeof(HttpClientFactoryServiceCollectionExtensions).Assembly,
            typeof(ILogger<>).Assembly,
            typeof(IServiceCollection).Assembly
        )
    );

    /// <summary>An SMS provider in the template's exact shape: options + validator + sender with the standard ctor.</summary>
    public const string SmsProviderSource = """
        using System.Net.Http;
        using System.Threading;
        using System.Threading.Tasks;
        using FluentValidation;
        using Headless.Http.Effects;
        using Headless.Sms;
        using Microsoft.Extensions.Logging;
        using Microsoft.Extensions.Options;

        namespace Acme.Sms;

        [OutboundEffect(OutboundEffect.Unsafe)]
        [GenerateProviderSetup("UseAcme", "Headless:AcmeSms", "AcmeSmsSender", "AcmeSmsOptionsValidator")]
        public sealed class AcmeSmsOptions
        {
            public string ApiKey { get; set; } = "";
        }

        internal sealed class AcmeSmsOptionsValidator : AbstractValidator<AcmeSmsOptions>
        {
            public AcmeSmsOptionsValidator() => RuleFor(x => x.ApiKey).NotEmpty();
        }

        internal sealed class AcmeSmsSender(
            IHttpClientFactory httpClientFactory,
            string httpClientName,
            IOptionsMonitor<AcmeSmsOptions> optionsMonitor,
            string? optionsName,
            ILogger<AcmeSmsSender> logger
        ) : ISmsSender, IBulkSmsSender
        {
            public ValueTask<SendSingleSmsResponse> SendAsync(SendSingleSmsRequest request, CancellationToken cancellationToken = default) =>
                throw new System.NotImplementedException();

            public ValueTask<SendBulkSmsResponse> SendBulkAsync(SendBulkSmsRequest request, CancellationToken cancellationToken = default) =>
                throw new System.NotImplementedException();
        }
        """;

    /// <summary>A single-backend client in the client template's shape, with its mandatory service hook.</summary>
    public const string ClientSource = """
        using FluentValidation;
        using Headless.Http.Effects;
        using Microsoft.Extensions.DependencyInjection;

        namespace Acme.Payments.Models
        {
            [OutboundEffect(OutboundEffect.Unsafe)]
            [GenerateClientSetup("AddAcmePayments", "Headless:AcmePayments", "AcmePaymentsOptionsValidator")]
            public sealed record AcmePaymentsOptions
            {
                public string ApiBaseUrl { get; init; } = "";
            }

            internal sealed class AcmePaymentsOptionsValidator : AbstractValidator<AcmePaymentsOptions>
            {
                public AcmePaymentsOptionsValidator() => RuleFor(x => x.ApiBaseUrl).NotEmpty();
            }
        }

        namespace Acme.Payments
        {
            public static partial class SetupAcmePayments
            {
                private static partial void _AddServices(IServiceCollection services) { }
            }
        }
        """;

    public static GeneratorDriver Run(
        string source,
        out ImmutableArray<Diagnostic> diagnostics,
        string assemblyName = "Acme.Sms"
    ) => Run([source], out diagnostics, assemblyName);

    public static GeneratorDriver Run(
        IReadOnlyList<string> sources,
        out ImmutableArray<Diagnostic> diagnostics,
        string assemblyName = "Acme.Sms"
    )
    {
        var compilation = CreateCompilation(assemblyName, sources);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new ProviderSetupGenerator().AsSourceGenerator()],
            parseOptions: GeneratorCompilation.ParseOptions
        );

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);
        diagnostics = output.GetDiagnostics().AddRange(generatorDiagnostics);

        return driver;
    }

    public static CSharpCompilation CreateCompilation(string assemblyName, IReadOnlyList<string> sources) =>
        CreateCompilation(assemblyName, [.. sources.Select((source, index) => ($"{assemblyName}.{index}.cs", source))]);

    public static CSharpCompilation CreateCompilation(
        string assemblyName,
        IReadOnlyList<(string Path, string Source)> sources
    ) => GeneratorCompilation.Create(assemblyName, sources, _References.Value);
}

// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Generator.ProviderSetup.Models;
using Headless.SourceGenerators;
using Microsoft.CodeAnalysis.CSharp;

namespace Headless.Generator.ProviderSetup.Emitting;

#pragma warning disable MA0076 // Generated source lines are clearer as interpolated templates.

/// <summary>
/// Writes the registration surface for one <c>[GenerateClientSetup]</c> options class: the <c>Add{Feature}</c>
/// overload trio on <c>IServiceCollection</c>, options validation, the named HttpClient with its resilience pipeline
/// derived from the declared effect, and a call into the package's mandatory <c>AddServices</c> hook.
/// </summary>
internal static class ClientSetupEmitter
{
    public static string GetFeatureName(ClientSetupModel model) =>
        model.AddMethodName.StartsWith("Add", StringComparison.Ordinal)
            ? model.AddMethodName.Substring(3)
            : model.AddMethodName;

    public static string Emit(ClientSetupModel model)
    {
        var writer = new SourceCodeBuilder();
        var feature = GetFeatureName(model);
        var optionsCref = model.OptionsTypeName.Replace("global::", string.Empty);
        var effectName = model.Effect.Replace("global::Headless.Http.Effects.", string.Empty);

        writer.AppendSourceHeader("Headless.Generator.ProviderSetup");
        writer.AppendLine("using Headless.Checks;");
        writer.AppendLine("using Headless.Http.Effects;");
        writer.AppendLine("using Microsoft.Extensions.Configuration;");
        writer.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        writer.AppendLine("using Microsoft.Extensions.DependencyInjection.Extensions;");
        writer.NewLine();
        writer.AppendLine($"namespace {model.SetupNamespace};");
        writer.NewLine();
        writer.AppendLine($"/// <summary>Generated registration surface for {feature}.</summary>");
        writer.AppendLine("[global::JetBrains.Annotations.PublicAPI]");
        writer.AppendLine($"public static partial class Setup{feature}");
        writer.OpenBracket();
        writer.AppendLine(
            $"internal const string HttpClientName = {SymbolDisplay.FormatLiteral(model.HttpClientName, quote: true)};"
        );
        writer.NewLine();

        _WriteOverload(
            writer,
            model,
            $"Action<{model.OptionsTypeName}> setupAction",
            "setupAction",
            $"configuring <see cref=\"{optionsCref}\"/> via a delegate",
            effectName
        );
        writer.NewLine();
        _WriteOverload(
            writer,
            model,
            $"Action<{model.OptionsTypeName}, IServiceProvider> setupAction",
            "setupAction",
            $"configuring <see cref=\"{optionsCref}\"/> with access to the service provider",
            effectName
        );
        writer.NewLine();
        _WriteOverload(
            writer,
            model,
            "IConfiguration config",
            "config",
            $"binding and validating <see cref=\"{optionsCref}\"/> from configuration",
            effectName
        );
        writer.NewLine();

        writer.AppendLine(
            "/// <summary>Registers the package's own services (typed clients, authenticators). Implemented by the package.</summary>"
        );
        writer.AppendLine("private static partial void AddServices(IServiceCollection services);");
        writer.NewLine();

        writer.AppendLine("private static IServiceCollection _AddCore(");
        writer.AppendLine("    IServiceCollection services,");
        writer.AppendLine("    Action<global::System.Net.Http.HttpClient>? configureClient,");
        writer.AppendLine(
            "    Action<global::Microsoft.Extensions.Http.Resilience.HttpStandardResilienceOptions>? configureResilience"
        );
        writer.AppendLine(")");
        writer.OpenBracket();
        writer.AppendLine("services.TryAddSingleton(TimeProvider.System);");
        writer.NewLine();
        writer.AppendLine("var httpClientBuilder = configureClient is null");
        writer.AppendLine("    ? services.AddHttpClient(HttpClientName)");
        writer.AppendLine("    : services.AddHttpClient(HttpClientName, configureClient);");
        writer.NewLine();
        writer.AppendLine(
            "// Derived from the options class's [OutboundEffect]; host-wide default handlers are removed first."
        );
        writer.AppendLine("httpClientBuilder.AddEffectResilienceHandler(");
        writer.AppendLine($"    {model.Effect},");
        writer.AppendLine(
            $"    idempotencyHeader: {(model.IdempotencyHeader is null ? "null" : SymbolDisplay.FormatLiteral(model.IdempotencyHeader, quote: true))},"
        );
        writer.AppendLine("    configureResilience: configureResilience");
        writer.AppendLine(");");
        writer.NewLine();
        writer.AppendLine("AddServices(services);");
        writer.NewLine();
        writer.AppendLine("return services;");
        writer.CloseBracket();
        writer.CloseBracket();

        return writer.ToString();
    }

    private static void _WriteOverload(
        SourceCodeBuilder writer,
        ClientSetupModel model,
        string argument,
        string argumentName,
        string how,
        string effectName
    )
    {
        var isConfig = argumentName == "config";

        writer.AppendLine($"/// <summary>Registers {GetFeatureName(model)} services, {how}.</summary>");
        writer.AppendLine(
            $"/// <remarks>Options are validated on startup. The HTTP resilience pipeline is derived from the declared <c>{effectName}</c> effect; <paramref name=\"configureResilience\"/> runs after the derived defaults.</remarks>"
        );
        writer.AppendLine(
            "/// <param name=\"services\">The <see cref=\"IServiceCollection\"/> to add the services to.</param>"
        );
        writer.AppendLine(
            isConfig
                ? "/// <param name=\"config\">The configuration section that contains the options.</param>"
                : "/// <param name=\"setupAction\">Delegate that configures the options.</param>"
        );
        writer.AppendLine(
            "/// <param name=\"configureClient\">Optional delegate to customise the internal <c>HttpClient</c>.</param>"
        );
        writer.AppendLine(
            "/// <param name=\"configureResilience\">Optional delegate applied after the derived resilience defaults.</param>"
        );
        writer.AppendLine(
            "/// <returns>The <see cref=\"IServiceCollection\"/> so that additional calls can be chained.</returns>"
        );
        writer.AppendLine(
            $"/// <exception cref=\"global::System.ArgumentNullException\"><paramref name=\"services\"/> or <paramref name=\"{argumentName}\"/> is <see langword=\"null\"/>.</exception>"
        );
        writer.AppendLine($"public static IServiceCollection {model.AddMethodName}(");
        writer.AppendLine("    this IServiceCollection services,");
        writer.AppendLine($"    {argument},");
        writer.AppendLine("    Action<global::System.Net.Http.HttpClient>? configureClient = null,");
        writer.AppendLine(
            "    Action<global::Microsoft.Extensions.Http.Resilience.HttpStandardResilienceOptions>? configureResilience = null"
        );
        writer.AppendLine(")");
        writer.OpenBracket();
        writer.AppendLine("Argument.IsNotNull(services);");
        writer.AppendLine($"Argument.IsNotNull({argumentName});");
        writer.NewLine();
        writer.AppendLine($"services.Configure<{model.OptionsTypeName}, {model.ValidatorTypeName}>({argumentName});");
        writer.NewLine();
        writer.AppendLine("return _AddCore(services, configureClient, configureResilience);");
        writer.CloseBracket();
    }
}

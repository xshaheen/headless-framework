// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Generator.ProviderSetup.Models;
using Headless.SourceGenerators;
using Microsoft.CodeAnalysis.CSharp;

namespace Headless.Generator.ProviderSetup.Emitting;

#pragma warning disable MA0076 // Generated source lines are clearer as interpolated templates.

/// <summary>
/// Writes the registration surface for one <c>[GenerateProviderSetup]</c> options class: the default and named
/// <c>Use{Provider}</c> overload trios, options wiring with FluentValidation startup validation, the named
/// HttpClient whose resilience pipeline is derived from the declared effect, and the default/keyed sender
/// registrations. This is the generator's only emit path.
/// </summary>
internal static class ProviderSetupEmitter
{
    public static string Emit(ProviderSetupModel model)
    {
        var writer = new SourceCodeBuilder();

        writer.AppendSourceHeader("Headless.Generator.ProviderSetup");
        writer.AppendLine("#pragma warning disable IDE0130 // registration surface lives in the family root namespace");
        writer.NewLine();

        writer.AppendLine("using Headless.Checks;");
        writer.AppendLine("using Headless.Http.Effects;");
        writer.AppendLine("using Headless.Sms;");
        writer.AppendLine($"using {model.OptionsTypeNamespace};");
        writer.AppendLine("using Microsoft.Extensions.Configuration;");
        writer.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        writer.AppendLine("using Microsoft.Extensions.DependencyInjection.Extensions;");
        writer.AppendLine("using Microsoft.Extensions.Logging;");
        writer.AppendLine("using Microsoft.Extensions.Options;");
        writer.NewLine();

        writer.AppendLine("namespace Headless.Sms;");
        writer.NewLine();

        _WriteDefaultHolder(writer, model);
        writer.NewLine();
        _WriteNamedHolder(writer, model);

        return writer.ToString();
    }

    /// <summary>The default (unkeyed) provider holder: extension members on HeadlessSmsSetupBuilder.</summary>
    private static void _WriteDefaultHolder(SourceCodeBuilder writer, ProviderSetupModel model)
    {
        writer.AppendLine(
            $"/// <summary>Generated registration surface for {model.UseMethodName}. Extension members on <see cref=\"HeadlessSmsSetupBuilder\"/>.</summary>"
        );
        writer.AppendLine("[global::JetBrains.Annotations.PublicAPI]");
        writer.AppendLine($"public static class Setup{GetProviderName(model)}");
        writer.OpenBracket();
        writer.AppendLine($"internal const string HttpClientName = {_Literal(model.HttpClientName)};");
        writer.NewLine();
        writer.AppendLine("extension(HeadlessSmsSetupBuilder setup)");
        writer.OpenBracket();

        _WriteUseOverload(writer, model, named: false, overload: Overload.Configuration);
        writer.NewLine();
        _WriteUseOverload(writer, model, named: false, overload: Overload.Action);
        writer.NewLine();
        _WriteUseOverload(writer, model, named: false, overload: Overload.ActionWithServiceProvider);

        writer.CloseBracket();
        writer.NewLine();
        _WriteCore(writer, model);
        writer.CloseBracket();
    }

    /// <summary>The named-instance provider holder: extension members on HeadlessSmsInstanceBuilder.</summary>
    private static void _WriteNamedHolder(SourceCodeBuilder writer, ProviderSetupModel model)
    {
        writer.AppendLine(
            $"/// <summary>Generated registration surface for named {model.UseMethodName} instances. Extension members on <see cref=\"HeadlessSmsInstanceBuilder\"/>.</summary>"
        );
        writer.AppendLine("[global::JetBrains.Annotations.PublicAPI]");
        writer.AppendLine($"public static class Setup{GetProviderName(model)}Named");
        writer.OpenBracket();
        writer.AppendLine("extension(HeadlessSmsInstanceBuilder instance)");
        writer.OpenBracket();

        _WriteUseOverload(writer, model, named: true, overload: Overload.Configuration);
        writer.NewLine();
        _WriteUseOverload(writer, model, named: true, overload: Overload.Action);
        writer.NewLine();
        _WriteUseOverload(writer, model, named: true, overload: Overload.ActionWithServiceProvider);

        writer.CloseBracket();
        writer.CloseBracket();
    }

    private enum Overload
    {
        Configuration,
        Action,
        ActionWithServiceProvider,
    }

    private static void _WriteUseOverload(
        SourceCodeBuilder writer,
        ProviderSetupModel model,
        bool named,
        Overload overload
    )
    {
        var receiver = named ? "instance" : "setup";
        var receiverType = named ? "HeadlessSmsInstanceBuilder" : "HeadlessSmsSetupBuilder";
        var optionsType = model.OptionsTypeName;
        var validatorType = model.ValidatorTypeName;

        string argument;
        string configureCall;
        string guard;

        switch (overload)
        {
            case Overload.Configuration:
                argument = "IConfiguration config";
                configureCall = $"(s, n) => s.Configure<{optionsType}, {validatorType}>(config, n)";
                guard = "config";
                break;
            case Overload.ActionWithServiceProvider:
                argument = $"Action<{optionsType}, IServiceProvider> setupAction";
                configureCall = $"(s, n) => s.Configure<{optionsType}, {validatorType}>(setupAction, n)";
                guard = "setupAction";
                break;
            default:
                argument = $"Action<{optionsType}> setupAction";
                configureCall = $"(s, n) => s.Configure<{optionsType}, {validatorType}>(setupAction, n)";
                guard = "setupAction";
                break;
        }

        var provider = GetProviderName(model);
        var optionsCref = model.OptionsTypeName.Replace("global::", string.Empty);
        var target = named ? $"Uses {provider} for this named instance" : $"Selects {provider}";
        var how = overload switch
        {
            Overload.Configuration => $"binding and validating <see cref=\"{optionsCref}\"/> from configuration",
            Overload.ActionWithServiceProvider =>
                $"configuring <see cref=\"{optionsCref}\"/> with access to the service provider",
            _ => $"configuring <see cref=\"{optionsCref}\"/> via a delegate",
        };

        writer.AppendLine($"/// <summary>{target}, {how}.</summary>");
        writer.AppendLine(
            $"/// <remarks>The HTTP resilience pipeline is derived from the declared <c>{model.Effect.Replace("global::Headless.Http.Effects.", string.Empty)}</c> effect; pass <paramref name=\"configureResilience\"/> to override it explicitly.</remarks>"
        );
        writer.AppendLine(
            overload == Overload.Configuration
                ? $"/// <param name=\"config\">Configuration section containing <see cref=\"{optionsCref}\"/> values.</param>"
                : "/// <param name=\"setupAction\">Delegate that populates the options.</param>"
        );
        writer.AppendLine(
            "/// <param name=\"configureClient\">Optional delegate to further configure the underlying <see cref=\"global::System.Net.Http.HttpClient\"/>.</param>"
        );
        writer.AppendLine(
            "/// <param name=\"configureResilience\">Optional delegate applied after the derived resilience defaults.</param>"
        );
        writer.AppendLine("/// <returns>The same builder, for chaining.</returns>");
        writer.AppendLine(
            $"/// <exception cref=\"global::System.ArgumentNullException\"><paramref name=\"{guard}\"/> is <see langword=\"null\"/>.</exception>"
        );
        writer.AppendLine($"public {receiverType} {model.UseMethodName}(");
        writer.AppendLine($"    {argument},");
        writer.AppendLine("    Action<global::System.Net.Http.HttpClient>? configureClient = null,");
        writer.AppendLine(
            "    Action<global::Microsoft.Extensions.Http.Resilience.HttpStandardResilienceOptions>? configureResilience = null"
        );
        writer.AppendLine(")");
        writer.OpenBracket();

        writer.AppendLine($"Argument.IsNotNull({guard});");
        writer.NewLine();

        if (named)
        {
            writer.AppendLine("var name = instance.Name;");
            writer.NewLine();
        }

        writer.AppendLine($"{receiver}.Register{(named ? "Provider" : "DefaultProvider")}(services =>");
        writer.AppendLine($"    Setup{GetProviderName(model)}.Add{GetProviderName(model)}SmsCore(");
        writer.AppendLine("        services,");
        writer.AppendLine(named ? "        name," : "        name: null,");
        writer.AppendLine($"        {configureCall},");
        writer.AppendLine("        configureClient,");
        writer.AppendLine("        configureResilience");
        writer.AppendLine("    )");
        writer.AppendLine(");");
        writer.NewLine();
        writer.AppendLine($"return {receiver};");
        writer.CloseBracket();
    }

    private static void _WriteCore(SourceCodeBuilder writer, ProviderSetupModel model)
    {
        writer.AppendLine(
            "/// <summary>Registers the provider sender; the resilience pipeline is derived from the declared effect.</summary>"
        );
        writer.AppendLine($"internal static void Add{GetProviderName(model)}SmsCore(");
        writer.AppendLine("    IServiceCollection services,");
        writer.AppendLine("    string? name,");
        writer.AppendLine("    Action<IServiceCollection, string?> configureOptions,");
        writer.AppendLine("    Action<global::System.Net.Http.HttpClient>? configureClient,");
        writer.AppendLine(
            "    Action<global::Microsoft.Extensions.Http.Resilience.HttpStandardResilienceOptions>? configureResilience"
        );
        writer.AppendLine(")");
        writer.OpenBracket();
        writer.AppendLine("configureOptions(services, name);");
        writer.NewLine();
        writer.AppendLine("var httpClientName = GetHttpClientName(name);");
        writer.NewLine();
        writer.AppendLine("var httpClientBuilder = configureClient is null");
        writer.AppendLine("    ? services.AddHttpClient(httpClientName)");
        writer.AppendLine("    : services.AddHttpClient(httpClientName, configureClient);");
        writer.NewLine();
        writer.AppendLine(
            "// The resilience pipeline is derived from the options class's [OutboundEffect] declaration;"
        );
        writer.AppendLine(
            "// the derivation removes any host-wide default handler first, so the declaration always wins."
        );
        writer.AppendLine("httpClientBuilder.AddEffectResilienceHandler(");
        writer.AppendLine($"    {model.Effect},");
        writer.AppendLine(
            $"    idempotencyHeader: {(model.IdempotencyHeader is null ? "null" : _Literal(model.IdempotencyHeader))},"
        );
        writer.AppendLine("    configureResilience: configureResilience");
        writer.AppendLine(");");
        writer.NewLine();
        if (model.SenderParameters.Any(static p => p.Kind == SenderParameterKind.TimeProvider))
        {
            // The sender resolves TimeProvider, so the registration must guarantee one exists.
            writer.AppendLine("services.TryAddSingleton(TimeProvider.System);");
            writer.NewLine();
        }

        writer.AppendLine("if (name is null)");
        writer.OpenBracket();
        writer.AppendLine(
            "services.AddSingleton<ISmsSender>(static sp => _CreateSender(sp, HttpClientName, optionsName: null));"
        );

        if (model.RegistersBulkForward)
        {
            writer.AppendLine(
                "services.AddSingleton<IBulkSmsSender>(static sp => (IBulkSmsSender)sp.GetRequiredService<ISmsSender>());"
            );
        }

        writer.NewLine();
        writer.AppendLine("return;");
        writer.CloseBracket();
        writer.NewLine();
        writer.AppendLine(
            "services.AddKeyedSingleton<ISmsSender>(name, (sp, _) => _CreateSender(sp, httpClientName, name));"
        );

        if (model.RegistersBulkForward)
        {
            writer.AppendLine("services.AddKeyedSingleton<IBulkSmsSender>(");
            writer.AppendLine("    name,");
            writer.AppendLine("    (sp, _) => (IBulkSmsSender)sp.GetRequiredKeyedService<ISmsSender>(name)");
            writer.AppendLine(");");
        }

        writer.CloseBracket();
        writer.NewLine();
        writer.AppendLine(
            "/// <summary>Each named instance gets its own client, and so its own resilience pipeline.</summary>"
        );
        writer.AppendLine("internal static string GetHttpClientName(string? name)");
        writer.OpenBracket();
        writer.AppendLine("return name is null ? HttpClientName : $\"{HttpClientName}:{name}\";");
        writer.CloseBracket();
        writer.NewLine();
        _WriteCreateSender(writer, model);
    }

    private static void _WriteCreateSender(SourceCodeBuilder writer, ProviderSetupModel model)
    {
        // Every factory reads the options snapshot for its own name, so keyed settings never bleed across instances.
        writer.AppendLine($"private static {model.SenderTypeName} _CreateSender(");
        writer.AppendLine("    IServiceProvider sp,");
        writer.AppendLine("    string httpClientName,");
        writer.AppendLine("    string? optionsName");
        writer.AppendLine(")");
        writer.OpenBracket();
        writer.Append($"return new {model.SenderTypeName}(");

        var arguments = new List<string>();

        foreach (var parameter in model.SenderParameters)
        {
            arguments.Add(
                parameter.Kind switch
                {
                    SenderParameterKind.HttpClientFactory => "sp.GetRequiredService<IHttpClientFactory>()",
                    SenderParameterKind.HttpClientNameString => "httpClientName",
                    SenderParameterKind.OptionsMonitor =>
                        $"sp.GetRequiredService<IOptionsMonitor<{model.OptionsTypeName}>>()",
                    SenderParameterKind.OptionsNameString => "optionsName",
                    SenderParameterKind.Logger => $"sp.GetRequiredService<ILogger<{model.SenderTypeName}>>()",
                    SenderParameterKind.TimeProvider => "sp.GetRequiredService<TimeProvider>()",
                    _ => throw new InvalidOperationException($"Unknown parameter kind {parameter.Kind}"),
                }
            );
        }

        writer.Append(string.Join(", ", arguments));
        writer.AppendLine(");");
        writer.CloseBracket();
    }

    // "UseInfobip" -> "Infobip": the holder classes keep their established Setup{Provider} names.
    internal static string GetProviderName(ProviderSetupModel model) =>
        model.UseMethodName.StartsWith("Use", StringComparison.Ordinal)
            ? model.UseMethodName.Substring(3)
            : model.UseMethodName;

    private static string _Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);
}

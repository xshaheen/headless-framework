// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Microsoft.CodeAnalysis;

namespace Tests;

/// <summary>Pins what the generator emits, that it compiles, and the diagnostics that bound the template.</summary>
public sealed class ProviderSetupGeneratorTests : TestBase
{
    [Fact]
    public Task should_emit_sms_provider_registration_surface()
    {
        var driver = GeneratorTestHelper.Run(GeneratorTestHelper.SmsProviderSource, out var diagnostics);

        diagnostics.Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);

        return Verify(driver).UseDirectory("Snapshots");
    }

    [Fact]
    public Task should_emit_single_backend_client_registration_surface()
    {
        var driver = GeneratorTestHelper.Run(GeneratorTestHelper.ClientSource, out var diagnostics, "Acme.Payments");

        diagnostics.Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);

        return Verify(driver).UseDirectory("Snapshots");
    }

    [Fact]
    public void should_emit_no_reflection_based_registration()
    {
        // Trimming/AOT: the generated surface constructs senders with `new` and resolves services by closed generic
        // type, so nothing needs reflection metadata that trimming could remove.
        var sms = GeneratorTestHelper.Run(GeneratorTestHelper.SmsProviderSource, out _).GetRunResult();
        var client = GeneratorTestHelper.Run(GeneratorTestHelper.ClientSource, out _, "Acme.Payments").GetRunResult();

        var generated = sms.GeneratedTrees.Concat(client.GeneratedTrees).Select(t => t.ToString()).ToList();

        generated.Should().NotBeEmpty();
        generated
            .Should()
            .AllSatisfy(source =>
            {
                source.Should().NotContain("Activator.");
                source.Should().NotContain("MakeGenericType");
                source.Should().NotContain("GetType(");
                source.Should().NotContain("typeof(");
                source.Should().NotContain("System.Reflection");
            });
    }

    [Fact]
    public void should_report_hp003_when_options_declare_no_effect()
    {
        var source = GeneratorTestHelper.SmsProviderSource.Replace(
            "[OutboundEffect(OutboundEffect.Unsafe)]",
            string.Empty,
            StringComparison.Ordinal
        );

        var run = GeneratorTestHelper.Run(source, out var diagnostics).GetRunResult();

        diagnostics.Select(d => d.Id).Should().Contain("HP003");
        run.GeneratedTrees.Should().BeEmpty("no setup is emitted without an effect declaration");
    }

    [Fact]
    public void should_report_hp004_when_sender_needs_a_dependency_outside_the_template()
    {
        // The Twilio shape: the sender takes an SDK client the template cannot construct, so the provider keeps a
        // hand-written setup instead of getting a silently mis-wired one.
        var source = GeneratorTestHelper.SmsProviderSource.Replace(
            "IHttpClientFactory httpClientFactory,",
            "IHttpClientFactory httpClientFactory, System.IComparable sdkClient,",
            StringComparison.Ordinal
        );

        var run = GeneratorTestHelper.Run(source, out var diagnostics).GetRunResult();

        diagnostics.Select(d => d.Id).Should().Contain("HP004");
        run.GeneratedTrees.Should().BeEmpty();
    }

    [Fact]
    public void should_report_hp005_when_two_options_declare_the_same_use_method()
    {
        // The same provider declared again in another namespace: both would emit UseAcme extension members.
        var duplicate = GeneratorTestHelper.SmsProviderSource.Replace(
            "namespace Acme.Sms;",
            "namespace Acme.Sms.Duplicate;",
            StringComparison.Ordinal
        );

        GeneratorTestHelper.Run([GeneratorTestHelper.SmsProviderSource, duplicate], out var diagnostics);

        diagnostics.Select(d => d.Id).Should().Contain("HP005");
    }
}

// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Logging;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class LogTextMaskerTests : TestBase
{
    private const string _NationalId = "29001011234567";

    [Fact]
    public void should_apply_a_pattern_whatever_log_scope_it_was_registered_with()
    {
        // given: a pattern scoped to one category and a high level, which a span or an error tracker does not have
        using var provider = _Build(static setup =>
            setup.MaskText(
                @"\b\d{14}\b",
                scope: static scope =>
                {
                    scope.MinimumLevel = LogLevel.Critical;
                    scope.Categories.Add("Payments");
                }
            )
        );
        var masker = provider.GetRequiredService<LogTextMasker>();

        // when
        var text = masker.Mask($"no citizen {_NationalId}");

        // then
        masker.IsEnabled.Should().BeTrue();
        text.Should().Be("no citizen ***");
    }

    [Fact]
    public void should_mask_values_the_way_log_properties_are_masked()
    {
        // given
        using var provider = _Build(static setup => setup.MaskText(@"\b\d{14}\b"));
        var masker = provider.GetRequiredService<LogTextMasker>();

        // then: a number renders to the id and is masked; values that cannot hold one keep their type
        masker.MaskValue(29001011234567L).Should().Be("***");
        masker.MaskValue(42).Should().Be(42);
        masker.MaskValue(Guid.Empty).Should().Be(Guid.Empty);
        masker.MaskValue(null).Should().BeNull();
    }

    [Fact]
    public void should_return_its_arguments_when_no_text_redactor_is_registered()
    {
        // given
        using var provider = _Build(setup: null);
        var masker = provider.GetRequiredService<LogTextMasker>();
        var exception = new InvalidOperationException($"no citizen {_NationalId}");

        // then
        masker.IsEnabled.Should().BeFalse();
        masker.Mask($"no citizen {_NationalId}").Should().Be($"no citizen {_NationalId}");
        masker.Mask(exception).Should().BeSameAs(exception);
        masker.MaskValue(29001011234567L).Should().Be(29001011234567L);
    }

    [Fact]
    public void should_include_a_redactor_registered_directly_in_the_container()
    {
        // given: a library that registers its own redactor without the builder
        var services = new ServiceCollection();
        services.AddSingleton<ILogTextRedactor, UpperCaseRedactor>();
        services.AddHeadlessRedaction();
        using var provider = services.BuildServiceProvider();

        // when
        var text = provider.GetRequiredService<LogTextMasker>().Mask("quiet");

        // then
        text.Should().Be("QUIET");
    }

    [Fact]
    public void should_throw_when_arguments_are_null()
    {
        using var provider = _Build(setup: null);
        var masker = provider.GetRequiredService<LogTextMasker>();

        var nullText = () => masker.Mask((string)null!);
        var nullException = () => masker.Mask((Exception)null!);

        nullText.Should().Throw<ArgumentNullException>();
        nullException.Should().Throw<ArgumentNullException>();
    }

    private static ServiceProvider _Build(Action<HeadlessRedactionSetupBuilder>? setup)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessRedaction(setup);

        return services.BuildServiceProvider();
    }

    private sealed class UpperCaseRedactor : ILogTextRedactor
    {
        public string Redact(string text) => text.ToUpperInvariant();
    }
}

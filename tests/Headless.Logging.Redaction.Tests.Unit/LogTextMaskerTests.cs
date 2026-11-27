// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.Reflection;
using Headless.Hosting;
using Headless.Logging;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class LogTextMaskerTests : TestBase
{
    private const string _NationalId = "29001011234567";

    private static readonly FieldInfo _DataField = typeof(Exception).GetField(
        "_data",
        BindingFlags.NonPublic | BindingFlags.Instance
    )!;

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
    public void should_return_a_placeholder_for_a_value_whose_text_throws()
    {
        // given
        using var provider = _Build(static setup => setup.MaskText(@"\b\d{14}\b"));
        var masker = provider.GetRequiredService<LogTextMasker>();

        // when
        var masked = masker.MaskValue(new UnprintableValue());

        // then: the value is withheld rather than passed on unscanned
        masked.Should().Be($"{typeof(UnprintableValue)} (redaction failed: InvalidOperationException)");
    }

    [Fact]
    public void should_mask_a_numeric_data_value_without_changing_the_original()
    {
        // given: an id held as a long, with messages that match nothing
        using var provider = _Build(static setup => setup.MaskText(@"\b\d{14}\b"));
        var masker = provider.GetRequiredService<LogTextMasker>();
        var original = new InvalidOperationException("lookup failed");
        original.Data["Citizen"] = 29001011234567L;
        original.Data["Order"] = 42;
        original.Data["Tracking"] = Guid.Empty;
        original.Data["Retried"] = true;

        // when
        var masked = masker.Mask(original);

        // then: the long is masked; a short number, a GUID, and a boolean pass through with their own types
        masked.Should().NotBeSameAs(original).And.BeOfType<InvalidOperationException>();
        masked.Data["Citizen"].Should().Be("***");
        masked.Data["Order"].Should().Be(42);
        masked.Data["Tracking"].Should().Be(Guid.Empty);
        masked.Data["Retried"].Should().Be(true);
        original.Data["Citizen"].Should().Be(29001011234567L);
    }

    [Fact]
    public void should_return_the_exception_itself_when_neither_messages_nor_data_match()
    {
        using var provider = _Build(static setup => setup.MaskText(@"\b\d{14}\b"));
        var masker = provider.GetRequiredService<LogTextMasker>();
        var original = new InvalidOperationException("lookup failed");
        original.Data["Order"] = 42;
        original.Data["Retried"] = true;

        masker.Mask(original).Should().BeSameAs(original);
    }

    [Fact]
    public void should_not_create_the_data_container_of_an_exception_that_has_none()
    {
        // given: an exception nothing has read Data from
        using var provider = _Build(static setup => setup.MaskText(@"\b\d{14}\b"));
        var masker = provider.GetRequiredService<LogTextMasker>();
        var original = new InvalidOperationException("lookup failed", new FormatException("bad input"));

        // when
        var masked = masker.Mask(original);

        // then: masking left both containers uncreated
        masked.Should().BeSameAs(original);
        _DataField.GetValue(original).Should().BeNull();
        _DataField.GetValue(original.InnerException).Should().BeNull();
    }

    [Fact]
    public void should_mask_data_held_by_a_type_that_overrides_data()
    {
        // given: a type that keeps its entries in a dictionary of its own
        using var provider = _Build(static setup => setup.MaskText(@"\b\d{14}\b"));
        var masker = provider.GetRequiredService<LogTextMasker>();
        var original = new OwnDataException();
        original.Data["Citizen"] = 29001011234567L;

        // when
        var masked = masker.Mask(original);

        // then
        masked.Should().NotBeSameAs(original);
        masked.Data["Citizen"].Should().Be("***");
        original.Data["Citizen"].Should().Be(29001011234567L);
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
    public void should_register_the_masker_under_the_hosting_contract()
    {
        // given: a feature that resolves the Hosting contract instead of referencing this package
        using var provider = _Build(static setup => setup.MaskText(@"\b\d{14}\b"));

        // when
        var contract = provider.GetService<ILogTextMasker>();

        // then
        contract.Should().BeSameAs(provider.GetRequiredService<LogTextMasker>());
        contract!.Mask($"no citizen {_NationalId}").Should().Be("no citizen ***");
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

    private sealed class UnprintableValue
    {
#pragma warning disable CA1065 // The fixture is a broken type on purpose: the test proves its throwing ToString is withheld.
        public override string ToString() => throw new InvalidOperationException("The value cannot be printed.");
#pragma warning restore CA1065
    }

    private sealed class UpperCaseRedactor : ILogTextRedactor
    {
        public string Redact(string text) => text.ToUpperInvariant();
    }

    private sealed class OwnDataException() : Exception("lookup failed")
    {
        private readonly Hashtable _entries = [];

        public override IDictionary Data => _entries;
    }
}

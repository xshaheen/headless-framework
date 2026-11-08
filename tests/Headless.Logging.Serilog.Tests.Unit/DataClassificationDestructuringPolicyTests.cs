// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Logging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Tests;

public sealed class DataClassificationDestructuringPolicyTests : TestBase
{
    private const string _LastName = "Mike";

    [Fact]
    public void should_replace_classified_property_with_redactor_output_when_destructured()
    {
        var structure = _DestructureStructure(new Payload { Note = "kept", LastName = _LastName });

        structure.TypeTag.Should().Be(nameof(Payload));
        structure.Scalar(nameof(Payload.Note)).Should().Be("kept");
        structure.Scalar(nameof(Payload.LastName)).Should().Be(_Pseudonym(_LastName));
    }

    [Fact]
    public void should_keep_erased_property_name_with_empty_value_when_redactor_erases()
    {
        var structure = _DestructureStructure(new Payload { TaxNumber = "1234567" });

        structure.HasProperty(nameof(Payload.TaxNumber)).Should().BeTrue();
        structure.Scalar(nameof(Payload.TaxNumber)).Should().Be(string.Empty);
    }

    [Fact]
    public void should_log_null_when_classified_property_is_null()
    {
        var structure = _DestructureStructure(new Payload { LastName = null });

        structure.HasProperty(nameof(Payload.LastName)).Should().BeTrue();
        structure.Scalar(nameof(Payload.LastName)).Should().BeNull();
    }

    [Fact]
    public void should_leave_type_to_serilog_when_no_member_is_classified()
    {
        var policy = new DataClassificationDestructuringPolicy(_CreateRedactorProvider());

        var handled = policy.TryDestructure(new Unclassified { Name = "raw" }, new ThrowingFactory(), out _);

        handled.Should().BeFalse();
    }

    [Fact]
    public void should_treat_no_data_classification_as_unclassified()
    {
        var policy = new DataClassificationDestructuringPolicy(_CreateRedactorProvider());

        var handled = policy.TryDestructure(new ExplicitlyPublic { Name = "raw" }, new ThrowingFactory(), out _);

        handled.Should().BeFalse();
    }

    [Fact]
    public void should_redact_nested_classified_property_inside_unclassified_parent()
    {
        var structure = _DestructureStructure(new Parent { Child = new Payload { LastName = _LastName } });

        var child = structure.Property(nameof(Parent.Child)).Should().BeOfType<StructureValue>().Subject;
        child.Scalar(nameof(Payload.LastName)).Should().Be(_Pseudonym(_LastName));
    }

    [Fact]
    public void should_redact_classified_property_declared_on_base_type()
    {
        var structure = _DestructureStructure(new DerivedPayload { LastName = _LastName, Extra = "e" });

        structure.TypeTag.Should().Be(nameof(DerivedPayload));
        structure.Scalar(nameof(Payload.LastName)).Should().Be(_Pseudonym(_LastName));
        structure.Scalar(nameof(DerivedPayload.Extra)).Should().Be("e");
    }

    [Fact]
    public void should_redact_override_when_classification_sits_on_base_declaration()
    {
        var structure = _DestructureStructure(new OverridingSecret { Value = _LastName });

        structure.Scalar(nameof(OverridingSecret.Value)).Should().Be(_Pseudonym(_LastName));
        structure.Properties.Should().ContainSingle(p => p.Name == nameof(OverridingSecret.Value));
    }

    [Fact]
    public void should_log_hidden_property_once_as_derived_declaration()
    {
        var structure = _DestructureStructure(new HidingSecret { Value = _LastName });

        structure.Properties.Should().ContainSingle(p => p.Name == nameof(HidingSecret.Value));
        structure.Scalar(nameof(HidingSecret.Value)).Should().Be(_LastName);
        structure.Scalar(nameof(HidingSecret.Other)).Should().Be(_Pseudonym("o"));
    }

    [Fact]
    public void should_skip_indexer_and_non_public_getter()
    {
        var structure = _DestructureStructure(new WithIndexer { LastName = _LastName });

        structure.Properties.Select(p => p.Name).Should().BeEquivalentTo(nameof(WithIndexer.LastName));
    }

    [Fact]
    public void should_report_throwing_getter_instead_of_failing_event()
    {
        var structure = _DestructureStructure(new ThrowingGetter());

        structure
            .Scalar(nameof(ThrowingGetter.Broken))
            .Should()
            .Be("The property accessor threw an exception: InvalidOperationException");
        structure.Scalar(nameof(ThrowingGetter.LastName)).Should().Be(_Pseudonym(_LastName));
    }

    [Fact]
    public void should_redact_with_combined_set_when_property_has_several_classifications()
    {
        var structure = _DestructureStructure(new DoublyClassified { Value = _LastName });

        // The combined set has no redactor of its own, so the provider's fallback (erasing) applies.
        structure.Scalar(nameof(DoublyClassified.Value)).Should().Be(string.Empty);
    }

    [Fact]
    public void should_redact_whole_value_when_type_is_classified()
    {
        var value = _Destructure(new Parent { Token = new ApiToken("secret-token") });

        var parent = value.Should().BeOfType<StructureValue>().Subject;
        parent.Scalar(nameof(Parent.Token)).Should().Be(_Pseudonym("ApiToken(secret-token)"));
    }

    [Fact]
    public void should_redact_classified_collection_element_by_element()
    {
        var structure = _DestructureStructure(new WithCollection { Names = ["a", null, "a"] });

        var sequence = structure.Property(nameof(WithCollection.Names)).Should().BeOfType<SequenceValue>().Subject;
        object?[] expected = [_Pseudonym("a"), null, _Pseudonym("a")];
        sequence.Elements.Cast<ScalarValue>().Select(e => e.Value).Should().Equal(expected);
    }

    [Fact]
    public void should_redact_classified_value_type_with_invariant_culture()
    {
        var structure = _DestructureStructure(new WithNumber { Amount = 1234.5m });

        structure.Scalar(nameof(WithNumber.Amount)).Should().Be(_Pseudonym("1234.5"));
    }

    [Fact]
    public void should_redact_destructured_values_when_reloadable_profile_finds_redactor_provider()
    {
        var services = new ServiceCollection().AddRedaction(_ConfigureRedaction);
        // A DI-registered policy that handles the same type must not bypass the classification.
        services.AddSingleton<IDestructuringPolicy, PayloadPassThroughPolicy>();
        using var provider = services.BuildServiceProvider();
        var sink = new CollectingSink();

        using (var logger = _CreateReloadableLogger(provider, sink))
        {
            logger.Information("Saw {@Payload}", new Payload { LastName = _LastName });
        }

        var structure = sink.SingleProperty("Payload").Should().BeOfType<StructureValue>().Subject;
        structure.Scalar(nameof(Payload.LastName)).Should().Be(_Pseudonym(_LastName));
    }

    [Fact]
    public void should_log_classified_values_unchanged_when_no_redactor_provider_is_registered()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var sink = new CollectingSink();

        using (var logger = _CreateReloadableLogger(provider, sink))
        {
            logger.Information("Saw {@Payload}", new Payload { LastName = _LastName });
        }

        var structure = sink.SingleProperty("Payload").Should().BeOfType<StructureValue>().Subject;
        structure.Scalar(nameof(Payload.LastName)).Should().Be(_LastName);
    }

    [Fact]
    public void should_throw_when_redactor_provider_is_null()
    {
        var act = () => new DataClassificationDestructuringPolicy(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static Logger _CreateReloadableLogger(IServiceProvider services, CollectingSink sink)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.ApplicationName.Returns("Headless.Logging.Serilog.Tests.Unit");
        environment.EnvironmentName.Returns(Environments.Production);

        return new LoggerConfiguration()
            .ConfigureReloadableLoggerConfiguration(
                services,
                new ConfigurationBuilder().Build(),
                environment,
                new SerilogOptions { WriteToFiles = false }
            )
            .WriteTo.Sink(sink)
            .CreateLogger();
    }

    private static StructureValue _DestructureStructure(object value)
    {
        return _Destructure(value).Should().BeOfType<StructureValue>().Subject;
    }

    private static LogEventPropertyValue _Destructure(object value)
    {
        var sink = new CollectingSink();

        using (
            var logger = new LoggerConfiguration()
                .Destructure.WithRedaction(_CreateRedactorProvider())
                .WriteTo.Sink(sink)
                .CreateLogger()
        )
        {
            logger.Information("{@Value}", value);
        }

        return sink.SingleProperty("Value");
    }

    private static IRedactorProvider _CreateRedactorProvider()
    {
        return new ServiceCollection()
            .AddRedaction(_ConfigureRedaction)
            .BuildServiceProvider()
            .GetRequiredService<IRedactorProvider>();
    }

    private static void _ConfigureRedaction(IRedactionBuilder redaction)
    {
        redaction.SetRedactor<PseudonymRedactor>(new DataClassificationSet(Taxonomy.Personal));
        redaction.SetRedactor<ErasingRedactor>(new DataClassificationSet(Taxonomy.Secret));
        redaction.SetFallbackRedactor<ErasingRedactor>();
    }

    private static string _Pseudonym(string value) => PseudonymRedactor.Format(value);

    // Deterministic and distinguishable from the input, which is all the policy tests need from a pseudonym.
    private sealed class PseudonymRedactor : Redactor
    {
        public const string Prefix = "p";

        public override int GetRedactedLength(ReadOnlySpan<char> input) => Format(input).Length;

        public override int Redact(ReadOnlySpan<char> source, Span<char> destination)
        {
            var formatted = Format(source);
            formatted.AsSpan().CopyTo(destination);
            return formatted.Length;
        }

        public static string Format(ReadOnlySpan<char> input) =>
            input.IsEmpty
                ? Prefix + "0:"
                : string.Create(CultureInfo.InvariantCulture, $"{Prefix}{input.Length}:{input[^1]}");
    }

    private static class Taxonomy
    {
        private const string _Name = "Tests";

        public static DataClassification Personal => new(_Name, nameof(Personal));

        public static DataClassification Secret => new(_Name, nameof(Secret));
    }

    private sealed class PersonalDataAttribute() : DataClassificationAttribute(Taxonomy.Personal);

    private sealed class SecretDataAttribute() : DataClassificationAttribute(Taxonomy.Secret);

    public class Payload
    {
        public string? Note { get; init; }

        [PersonalData]
        public string? LastName { get; init; }

        [SecretData]
        public string? TaxNumber { get; init; }
    }

    public sealed class DerivedPayload : Payload
    {
        public string? Extra { get; init; }
    }

    public sealed class Parent
    {
        public Payload? Child { get; init; }

        public ApiToken? Token { get; init; }
    }

    [PersonalData]
    public sealed record ApiToken(string Value)
    {
        public override string ToString() => $"ApiToken({Value})";
    }

    public sealed class Unclassified
    {
        public string? Name { get; init; }
    }

    public sealed class ExplicitlyPublic
    {
        [NoDataClassification]
        public string? Name { get; init; }
    }

    public class SecretBase
    {
        [PersonalData]
        public virtual string? Value { get; init; }
    }

    public sealed class OverridingSecret : SecretBase
    {
        public override string? Value { get; init; }
    }

    public class HiddenBase
    {
        [PersonalData]
        public string? Value { get; init; }

        [PersonalData]
        public string Other { get; init; } = "o";
    }

    // The derived declaration hides the classified base one without restating the classification.
    public sealed class HidingSecret : HiddenBase
    {
        public new string? Value { get; init; }
    }

    public sealed class WithIndexer
    {
        [PersonalData]
        public string? LastName { get; init; }

#pragma warning disable CA1044, CA1822, MA0041 // The shape under test needs an instance indexer and a private getter.
        public string this[int index] => index.ToString(CultureInfo.InvariantCulture);

        public string? WriteOnly { private get; set; }
#pragma warning restore CA1044, CA1822, MA0041
    }

    public sealed class ThrowingGetter
    {
        public string Broken => throw new InvalidOperationException("boom");

        [PersonalData]
        public string LastName => _LastName;
    }

    public sealed class DoublyClassified
    {
        [PersonalData]
        [SecretData]
        public string? Value { get; init; }
    }

    public sealed class WithCollection
    {
        [PersonalData]
        public IReadOnlyList<string?> Names { get; init; } = [];
    }

    public sealed class WithNumber
    {
        [PersonalData]
        public decimal Amount { get; init; }
    }

    private sealed class PayloadPassThroughPolicy : IDestructuringPolicy
    {
        public bool TryDestructure(
            object value,
            ILogEventPropertyValueFactory propertyValueFactory,
            out LogEventPropertyValue result
        )
        {
            if (value is Payload payload)
            {
                result = new StructureValue([
                    new LogEventProperty(nameof(Payload.LastName), new ScalarValue(payload.LastName)),
                ]);
                return true;
            }

            result = null!;
            return false;
        }
    }

    private sealed class ThrowingFactory : ILogEventPropertyValueFactory
    {
        public LogEventPropertyValue CreatePropertyValue(object? value, bool destructureObjects = false) =>
            throw new InvalidOperationException("The policy must not destructure a type it does not handle.");
    }

    private sealed class CollectingSink : ILogEventSink
    {
        private readonly List<LogEvent> _events = [];

        public void Emit(LogEvent logEvent) => _events.Add(logEvent);

        public LogEventPropertyValue SingleProperty(string name) =>
            _events.Should().ContainSingle().Subject.Properties[name];
    }
}

internal static class StructureValueTestExtensions
{
    public static LogEventPropertyValue Property(this StructureValue structure, string name) =>
        structure.Properties.Should().ContainSingle(p => p.Name == name).Subject.Value;

    public static bool HasProperty(this StructureValue structure, string name) =>
        structure.Properties.Any(p => string.Equals(p.Name, name, StringComparison.Ordinal));

    public static object? Scalar(this StructureValue structure, string name) =>
        structure.Property(name).Should().BeOfType<ScalarValue>().Subject.Value;
}

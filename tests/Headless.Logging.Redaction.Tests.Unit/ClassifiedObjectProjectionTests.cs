// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Logging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed partial class ClassifiedObjectProjectionTests : TestBase
{
    private const string _Email = "jane@example.test";

    [Fact]
    public void should_redact_a_classified_member_of_a_captured_record_in_text_and_properties()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);

        // when
        _Logger(provider).LogInformation("Saw {@Customer}", new Customer(_Email, "gold"));

        // then
        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry.Text.Should().Be("Saw Customer { Email = *****, Tier = gold }");
        entry.Properties["@Customer"].Should().Be("Customer { Email = *****, Tier = gold }");
    }

    [Fact]
    public void should_project_a_record_logged_without_the_capture_prefix()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);

        // when
        _Logger(provider).LogInformation("Saw {Customer}", new Customer(_Email, "gold"));

        // then
        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry.Text.Should().Be("Saw Customer { Email = *****, Tier = gold }");
        entry.Properties["Customer"].Should().Be("Customer { Email = *****, Tier = gold }");
    }

    [Fact]
    public void should_pass_an_object_without_classified_members_through_as_the_same_instance()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);
        var order = new Order(42, "open");

        // when
        _Logger(provider).LogInformation("Placed {@Order}", order);

        // then
        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry.Properties["@Order"].Should().BeSameAs(order);
        entry.Text.Should().Be("Placed Order { Id = 42, State = open }");
    }

    [Fact]
    public void should_redact_a_classified_member_nested_in_another_record()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);

        // when
        _Logger(provider).LogInformation("Got {@Envelope}", new Envelope("e-1", new Customer(_Email, "gold")));

        // then
        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry.Text.Should().Be("Got Envelope { Id = e-1, Payload = Customer { Email = *****, Tier = gold } }");
        entry.Text.Should().NotContain(_Email);
    }

    [Fact]
    public void should_redact_each_element_of_a_logged_collection()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);
        List<Customer> customers = [new(_Email, "gold"), new("bob@example.test", "silver")];

        // when
        _Logger(provider).LogInformation("Batch {Customers}", customers);

        // then
        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry
            .Text.Should()
            .Be("Batch Customer { Email = *****, Tier = gold }, Customer { Email = *****, Tier = silver }");
        entry.Text.Should().NotContain("example.test");
    }

    [Fact]
    public void should_redact_a_whole_value_whose_type_is_classified()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);

        // when
        _Logger(provider).LogInformation("Mail to {Address}", new MailAddress(_Email));

        // then
        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry.Text.Should().Be("Mail to *****");
        entry.Properties["Address"].Should().Be("*****");
    }

    [Fact]
    public void should_print_a_throwing_member_as_the_exception_type_without_failing_the_call()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);

        // when
        _Logger(provider).LogInformation("Saw {@Holder}", new ThrowingHolder { Email = _Email });

        // then
        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry
            .Properties["@Holder"]
            .Should()
            .Be(
                "ThrowingHolder { Email = *****, Broken = The property accessor threw an exception: InvalidOperationException }"
            );
    }

    [Fact]
    public void should_print_only_the_type_name_of_a_class_whose_to_string_may_show_a_classified_member()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);

        // when
        _Logger(provider).LogInformation("Saw {Contact}", new Contact { Email = _Email });

        // then
        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry.Text.Should().Be("Saw " + typeof(Contact));
        entry.Properties["Contact"].Should().Be(typeof(Contact).ToString());
    }

    [Fact]
    public void should_check_a_member_declared_as_object_by_the_value_it_holds()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);

        // when
        _Logger(provider).LogInformation("Got {@Box}", new Box(new Customer(_Email, "gold")));
        _Logger(provider).LogInformation("Got {@Box}", new Box(7));

        // then
        sink.Entries.Select(e => e.Text)
            .Should()
            .Equal("Got Box { Value = Customer { Email = *****, Tier = gold } }", "Got Box { Value = 7 }");
    }

    [Fact]
    public void should_redact_a_classified_parameter_and_an_object_in_one_logger_message()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);

        // when
        LogSignup(_Logger(provider), _Email, new ContactParty("Jane", _Email));

        // then
        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry.Text.Should().Be("Signup ***** as ContactParty { Name = Jane, Email = ***** }");
        entry.Properties["Email"].Should().Be("*****");
        entry.Properties["Party"].Should().Be("ContactParty { Name = Jane, Email = ***** }");
    }

    [Fact]
    public void should_redact_a_classified_member_of_an_object_in_a_scope()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);
        var logger = _Logger(provider);

        // when
        using (logger.BeginScope("For {@Customer}", new Customer(_Email, "gold")))
        {
            logger.LogInformation("inside");
        }

        // then
        sink.Scopes.SelectMany(s => s)
            .Should()
            .Contain(new KeyValuePair<string, object?>("@Customer", "Customer { Email = *****, Tier = gold }"));
    }

    [Fact]
    public void should_leave_an_object_raw_when_no_redactor_provider_is_registered()
    {
        // given: logging without AddHeadlessRedaction keeps the provider's own rendering
        using var sink = new CapturingProvider();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders().AddProvider(sink));
        using var provider = services.BuildServiceProvider();
        var customer = new Customer(_Email, "gold");

        // when
        _Logger(provider).LogInformation("Saw {@Customer}", customer);

        // then
        sink.Entries.Should().ContainSingle().Which.Properties["@Customer"].Should().BeSameAs(customer);
    }

    // The generator rejects a parameter whose declared type has a classified member (LOGGEN035), so the case that
    // reaches run time is a derived value passed through an unclassified base type.
    [LoggerMessage(1, LogLevel.Information, "Signup {Email} as {Party}")]
    private static partial void LogSignup(ILogger logger, [PrivateData] string email, Party party);

    private static ILogger _Logger(IServiceProvider provider) =>
        provider.GetRequiredService<ILoggerFactory>().CreateLogger("App");

    private static ServiceProvider _Build(CapturingProvider sink)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders().AddProvider(sink));
        services.AddHeadlessRedaction(setup =>
            setup.ConfigureRedaction(static redaction =>
                redaction.SetRedactor<StarRedactor>(new DataClassificationSet(HeadlessDataClassifications.Private))
            )
        );

        return services.BuildServiceProvider();
    }

    public sealed record Customer([property: PrivateData] string Email, string Tier);

    public sealed record Order(int Id, string State);

    public record Envelope(string Id, Customer Payload);

    public sealed record Box(object Value);

    public abstract record Party(string Name);

    public sealed record ContactParty(string Name, [property: PrivateData] string Email) : Party(Name);

    [PrivateData]
    public sealed class MailAddress(string value)
    {
        public override string ToString() => value;
    }

    public sealed class Contact
    {
        [PrivateData]
        public required string Email { get; init; }

        public override string ToString() => "Contact " + Email;
    }

    public sealed class ThrowingHolder
    {
        [PrivateData]
        public required string Email { get; init; }

        public string Broken => throw new InvalidOperationException("boom");
    }

    private sealed class StarRedactor : Redactor
    {
        public override int GetRedactedLength(ReadOnlySpan<char> input) => 5;

        public override int Redact(ReadOnlySpan<char> source, Span<char> destination)
        {
            "*****".AsSpan().CopyTo(destination);

            return 5;
        }
    }

    private sealed record Entry(string Text, Dictionary<string, object?> Properties);

    private sealed class CapturingProvider : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider? _scopes;

        public List<Entry> Entries { get; } = [];

        public List<IReadOnlyList<KeyValuePair<string, object?>>> Scopes { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

        public void Dispose() { }

        private sealed class Logger(CapturingProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => owner._scopes?.Push(state);

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
            {
                var properties = new Dictionary<string, object?>(StringComparer.Ordinal);

                if (state is IEnumerable<KeyValuePair<string, object?>> values)
                {
                    foreach (var (key, value) in values)
                    {
                        properties[key] = value;
                    }
                }

                owner._scopes?.ForEachScope(
                    static (scope, scopes) =>
                    {
                        if (scope is IEnumerable<KeyValuePair<string, object?>> values)
                        {
                            scopes.Add([.. values]);
                        }
                    },
                    owner.Scopes
                );

                owner.Entries.Add(new Entry(formatter(state, exception), properties));
            }
        }
    }
}

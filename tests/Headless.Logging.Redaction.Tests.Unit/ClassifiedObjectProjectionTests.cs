// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.Globalization;
using System.Net;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Headless.Logging;
using Headless.Logging.Internal;
using Headless.Testing.Tests;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed partial class ClassifiedObjectProjectionTests : TestBase
{
    private const string _Email = "jane@example.test";

    private const BindingFlags _DeclaredPublic =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

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

    [Fact]
    public void should_log_a_collection_type_that_enumerates_itself_without_overflowing_the_stack()
    {
        // given: a type whose element type is itself, plain and with a classified member
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);
        var plain = new PlainNode([new PlainNode([])]);
        var classified = new ClassifiedNode(_Email, [new ClassifiedNode(_Email, [])]);

        // when
        _Logger(provider).LogInformation("Saw {@Node}", plain);
        _Logger(provider).LogInformation("Saw {@Node}", classified);
        _Logger(provider).LogInformation("Saw {Tokens}", (object)Array.Empty<Token>());
        _Logger(provider).LogInformation("Saw {@Holder}", new TokenHolder(Root: null));

        // then
        sink.Entries.Should().HaveCount(4);
        sink.Entries.Should().AllSatisfy(e => e.Text.Should().NotContain(_Email));
    }

    [Fact]
    public void should_redact_a_positional_record_parameter_classified_without_a_property_target()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);

        // when
        _Logger(provider).LogInformation("Saw {@P}", new Positional(_Email, "gold"));

        // then
        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry.Text.Should().Be("Saw Positional { Email = *****, Tier = gold }");
    }

    [Fact]
    public void should_redact_a_classified_public_field_of_a_record()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);

        // when
        _Logger(provider).LogInformation("Saw {@X}", new FieldHolder("gold") { Email = _Email });

        // then
        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry.Text.Should().NotContain(_Email);
        entry.Text.Should().Be("Saw FieldHolder { Tier = gold, Email = ***** }");
    }

    [Fact]
    public void should_log_a_parent_with_hundreds_of_children_pointing_back_once()
    {
        // given: every child points back to its parent, so the graph is a cycle with a wide fan-out
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);
        var parent = new TreeNode { Email = _Email };

        for (var i = 0; i < 300; i++)
        {
            parent.Children.Add(new TreeNode { Email = _Email, Parent = parent });
        }

        // when
        _Logger(provider).LogInformation("Saw {@Tree}", parent);

        // then
        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry.Text.Should().NotContain(_Email);
        entry.Text.Should().StartWith("Saw TreeNode { Email = *****, Parent = , Children = TreeNode { Email = *****");
        entry.Text.Should().EndWith(", ... }");
    }

    [Fact]
    public void should_log_an_unclassified_cycle_with_hundreds_of_children_once()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);
        var parent = new PlainTreeNode();

        for (var i = 0; i < 300; i++)
        {
            parent.Children.Add(new PlainTreeNode { Parent = parent });
        }

        // when
        _Logger(provider).LogInformation("Saw {Tree}", parent);

        // then
        sink.Entries.Should().ContainSingle();
    }

    [Fact]
    public void should_project_a_child_that_reaches_classified_data_only_through_its_parent()
    {
        // given: the kid holds no classified member of its own; printing it raw would print the family raw
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);
        var family = new Family { Email = _Email };
        family.Kids.Add(new Kid { Owner = family });

        // when
        _Logger(provider).LogInformation("Saw {@Family}", family);

        // then
        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry.Text.Should().Be($"Saw Family {{ Email = *****, Kids = Kid {{ Owner = {typeof(Family)} }} }}");
    }

    [Fact]
    public void should_not_print_a_classified_member_nested_below_the_depth_cap_raw()
    {
        // given: a classified member further down than the walk looks
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);
        var chain = new Link(Next: null, Leaf: new Customer(_Email, "gold"));

        for (var i = 0; i < 10; i++)
        {
            chain = new Link(chain, Leaf: null);
        }

        // when
        _Logger(provider).LogInformation("Saw {@Chain}", chain);

        // then: the level the walk does not reach prints as its type name
        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry.Text.Should().NotContain(_Email).And.Contain(typeof(Link).ToString());
        entry.Properties["@Chain"].Should().BeOfType<string>().Which.Should().NotContain(_Email);
    }

    [Fact]
    public void should_redact_a_one_shot_sequence_without_enumerating_it_twice()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);
        List<Customer> list = [new(_Email, "gold"), new("bob@example.test", "silver")];
        var customers = list.AsEnumerableOnce();

        // when
        _Logger(provider).LogInformation("Batch {Customers}", customers);

        // then
        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry
            .Text.Should()
            .Be("Batch Customer { Email = *****, Tier = gold }, Customer { Email = *****, Tier = silver }");
    }

    [Fact]
    public void should_print_a_one_shot_sequence_of_unclassified_values_from_one_read()
    {
        // given: elements typed as object must be read to prove them safe, and the sequence gives them only once
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);
        List<object> values = [42, "gold"];
        var once = values.AsEnumerableOnce();

        // when
        _Logger(provider).LogInformation("Batch {Values}", once);

        // then
        sink.Entries.Should().ContainSingle().Which.Text.Should().Be("Batch 42, gold");
    }

    [Fact]
    public void should_run_a_lazy_query_of_unclassified_values_once()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);
        var evaluations = 0;
        object[] source = [1, "two"];
        var values = source.Select(value =>
        {
            evaluations++;

            return value;
        });

        // when
        _Logger(provider).LogInformation("Batch {Values}", values);

        // then
        evaluations.Should().Be(source.Length);
        sink.Entries.Should().ContainSingle().Which.Text.Should().Be("Batch 1, two");
    }

    [Fact]
    public void should_pass_a_collection_of_unclassified_values_through_as_the_same_instance()
    {
        // given: a collection can be read again, so the provider keeps the original value
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);
        List<object> values = [42, "gold"];

        // when
        _Logger(provider).LogInformation("Batch {Values}", values);

        // then
        sink.Entries.Should().ContainSingle().Which.Properties["Values"].Should().BeSameAs(values);
    }

    [Fact]
    public void should_run_a_logged_lazy_query_once()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);
        var evaluations = 0;
        Customer[] source = [new(_Email, "gold"), new("bob@example.test", "silver")];
        var customers = source.Select(customer =>
        {
            evaluations++;

            return customer;
        });

        // when
        _Logger(provider).LogInformation("Batch {Customers}", customers);

        // then
        evaluations.Should().Be(source.Length);
        sink.Entries.Should().ContainSingle().Which.Text.Should().NotContain("example.test");
    }

    [Fact]
    public void should_read_a_sequence_once_for_both_detection_and_projection()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);
        var projection = new ClassifiedObjectProjection(provider.GetRequiredService<IRedactorProvider>());
        var evaluations = 0;
        Customer[] source = [new(_Email, "gold"), new("bob@example.test", "silver")];
        var customers = source.Select(customer =>
        {
            evaluations++;

            return customer;
        });

        // when
        var projected = projection.TryProject(customers, captured: false, out var text);

        // then
        projected.Should().BeTrue();
        text.Should().Be("Customer { Email = *****, Tier = gold }, Customer { Email = *****, Tier = silver }");
        evaluations.Should().Be(source.Length);
    }

    [Fact]
    public void should_pass_a_sequence_of_safe_elements_through_as_the_same_instance()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);
        List<Order> orders = [new(1, "open"), new(2, "closed")];

        // when
        _Logger(provider).LogInformation("Placed {Orders}", orders);

        // then
        sink.Entries.Should().ContainSingle().Which.Properties["Orders"].Should().BeSameAs(orders);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("method")]
    [InlineData("property")]
    [InlineData("parameter")]
    [InlineData("assembly")]
    [InlineData("module")]
    [InlineData("uri")]
    [InlineData("version")]
    [InlineData("culture")]
    [InlineData("ip-address")]
    [InlineData("ip-endpoint")]
    [InlineData("time-zone")]
    [InlineData("encoding")]
    [InlineData("http-method")]
    [InlineData("string-builder")]
    [InlineData("cancellation-token")]
    [InlineData("exception")]
    [InlineData("file-info")]
    [InlineData("regex")]
    [InlineData("big-integer")]
    public void should_print_a_logged_bcl_value_as_the_provider_would(string kind)
    {
        // given
        object value = kind switch
        {
            "type" => typeof(Order),
            "method" => typeof(Order).GetMethod(nameof(Order.ToString), _DeclaredPublic, Type.EmptyTypes)!,
            "property" => typeof(Order).GetProperty(nameof(Order.Id), _DeclaredPublic)!,
            "parameter" => typeof(Order).GetConstructors()[0].GetParameters()[0],
            "assembly" => typeof(Order).Assembly,
            "module" => typeof(Order).Module,
            "uri" => new Uri("https://example.test/orders"),
            "version" => new Version(1, 2, 3),
            "culture" => CultureInfo.InvariantCulture,
            "ip-address" => IPAddress.Loopback,
            "ip-endpoint" => new IPEndPoint(IPAddress.Loopback, 8080),
            "time-zone" => TimeZoneInfo.Utc,
            "encoding" => Encoding.UTF8,
            "http-method" => HttpMethod.Get,
            "string-builder" => new StringBuilder("built"),
            "cancellation-token" => CancellationToken.None,
            "exception" => new InvalidOperationException("boom"),
            "file-info" => new FileInfo("orders.csv"),
            "regex" => new Regex("^a+$", RegexOptions.None, TimeSpan.FromSeconds(1)),
            "big-integer" => new BigInteger(42),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, message: null),
        };

        using var sink = new CapturingProvider();
        using var provider = _Build(sink);

        // when
        _Logger(provider).LogInformation("Saw {Value}", value);

        // then
        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry.Text.Should().Be("Saw " + value, kind);
        entry.Properties["Value"].Should().Be(value, kind);
    }

    [Fact]
    public void should_project_both_objects_when_a_member_getter_logs_while_its_owner_is_projected()
    {
        // given: reading Note logs another classified object through the same logger
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);
        var logger = _Logger(provider);

        // when
        logger.LogInformation("Saw {@Value}", new Chatty(logger));

        // then: the inner entry has its own walk and leaves the outer text intact
        sink.Entries.Select(e => e.Text)
            .Should()
            .Equal("Inner Customer { Email = *****, Tier = silver }", "Saw Chatty { Email = *****, Note = noted }");
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

    public sealed class Chatty(ILogger logger)
    {
        [PrivateData]
        public string Email => _Email;

        public string Note
        {
            get
            {
                logger.LogInformation("Inner {@Customer}", new Customer(_Email, "silver"));

                return "noted";
            }
        }
    }

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

    private sealed record Positional([PrivateData] string Email, string Tier);

    private sealed record FieldHolder(string Tier)
    {
        [PrivateData]
        public required string Email;
    }

    private sealed class PlainNode(List<PlainNode> children) : IEnumerable<PlainNode>
    {
        public IEnumerator<PlainNode> GetEnumerator() => children.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class ClassifiedNode(string email, List<ClassifiedNode> children) : IEnumerable<ClassifiedNode>
    {
        [PrivateData]
        public string Email { get; } = email;

        public IEnumerator<ClassifiedNode> GetEnumerator() => children.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // Shaped like a JSON token: an abstract type that enumerates values of its own type.
    private abstract class Token : IEnumerable<Token>
    {
        public abstract IEnumerator<Token> GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed record TokenHolder(Token? Root);

    private sealed record Link(Link? Next, Customer? Leaf);

    private sealed class TreeNode
    {
        [PrivateData]
        public required string Email { get; init; }

        public TreeNode? Parent { get; init; }

        public List<TreeNode> Children { get; } = [];
    }

    private sealed class PlainTreeNode
    {
        public PlainTreeNode? Parent { get; init; }

        public List<PlainTreeNode> Children { get; } = [];
    }

    private sealed class Family
    {
        [PrivateData]
        public required string Email { get; init; }

        public List<Kid> Kids { get; } = [];
    }

    private sealed class Kid
    {
        public required Family Owner { get; init; }
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

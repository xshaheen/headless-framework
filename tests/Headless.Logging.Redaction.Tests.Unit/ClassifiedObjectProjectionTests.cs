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
    public void should_print_a_one_shot_sequence_as_its_type_name_without_enumerating_it()
    {
        // given: a sequence that gives its elements once, so reading it would leave the provider nothing
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);
        var customers = new CountingSequence<Customer>([new(_Email, "gold"), new("bob@example.test", "silver")]);

        // when
        _Logger(provider).LogInformation("Batch {Customers}", customers);

        // then
        customers.Enumerations.Should().Be(0);
        sink.Entries.Should().ContainSingle().Which.Text.Should().Be("Batch " + typeof(CountingSequence<Customer>));
    }

    [Fact]
    public void should_not_run_a_logged_lazy_query()
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
        evaluations.Should().Be(0);
        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry.Text.Should().Be("Batch " + customers.GetType());
        entry.Properties["Customers"].Should().Be(customers.GetType().ToString());
    }

    [Fact]
    public void should_not_enumerate_a_lazy_sequence_reached_through_an_object_graph()
    {
        // given: shaped like a DbContext whose DbSet property would run a query when enumerated
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);
        var rows = new CountingSequence<Customer>([new(_Email, "gold")]);
        var store = new Store(rows);

        // when
        _Logger(provider).LogInformation("Saw {@Store}", store);

        // then
        rows.Enumerations.Should().Be(0);
        sink.Entries.Should()
            .ContainSingle()
            .Which.Text.Should()
            .Be($"Saw Store {{ Rows = {typeof(CountingSequence<Customer>)} }}");
    }

    [Fact]
    public void should_print_an_exception_argument_as_the_provider_would_without_walking_its_members()
    {
        // given: an exception whose members reach a sequence that throws when enumerated, as an EF Core
        // DbUpdateException reaches a DbSet whose entity the model excludes
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);
        var error = new PayloadException(new Store(new ThrowingSequence()));

        // when
        _Logger(provider).LogError("Save failed.{NewLine}{Error}", Environment.NewLine, error);

        // then
        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry.Text.Should().Be("Save failed." + Environment.NewLine + error);
        entry.Properties["Error"].Should().BeSameAs(error);
    }

    [Fact]
    public void should_print_a_placeholder_when_a_collection_enumerator_throws_without_failing_the_call()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);

        // when
        _Logger(provider).LogInformation("Saw {Batch}", new Batch(new ThrowingCollection()));

        // then
        sink.Entries.Should()
            .ContainSingle()
            .Which.Text.Should()
            .Be("Saw Batch { Rows = The enumerator threw an exception: InvalidOperationException }");
    }

    [Fact]
    public void should_print_a_placeholder_when_a_value_text_throws_inside_a_projection()
    {
        // given: a record with a classified member beside a value whose ToString throws
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);

        // when
        _Logger(provider).LogInformation("Saw {Profile}", new Profile(_Email, new Exploding()));

        // then: the whole value is withheld, and the entry is still logged
        sink.Entries.Should()
            .ContainSingle()
            .Which.Text.Should()
            .Be($"Saw {typeof(Profile)} (redaction failed: InvalidOperationException)");
    }

    [Fact]
    public void should_print_a_placeholder_when_a_classified_name_holds_a_sequence_that_throws()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(
            sink,
            static setup => setup.ClassifyProperty("Rows", HeadlessDataClassifications.Private)
        );

        // when
        _Logger(provider).LogInformation("Saw {Rows}", new ThrowingSequence());

        // then
        sink.Entries.Should()
            .ContainSingle()
            .Which.Text.Should()
            .Be($"Saw {typeof(ThrowingSequence)} (redaction failed: InvalidOperationException)");
    }

    [Fact]
    public void should_not_run_the_factory_of_a_lazy_value()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);
        var created = 0;
        var next = new Lazy<Customer>(() =>
        {
            created++;

            return new Customer(_Email, "gold");
        });

        // when
        _Logger(provider).LogInformation("Saw {@Pending}", new Pending(next));

        // then
        created.Should().Be(0);
        sink.Entries.Should().ContainSingle().Which.Text.Should().Be($"Saw Pending {{ Next = {next.GetType()} }}");
    }

    [Fact]
    public void should_not_read_the_navigations_of_an_entity_that_loads_them_lazily()
    {
        // given: an entity shaped the way EF Core injects its lazy loader, whose navigation getter runs a query
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);
        var order = new LazyOrder();

        // when
        _Logger(provider).LogInformation("Saw {@Order}", order);

        // then
        order.Loads.Should().Be(0);
        sink.Entries.Should().ContainSingle().Which.Text.Should().Be("Saw " + typeof(LazyOrder));
    }

    [Fact]
    public void should_run_each_getter_once_per_log_call()
    {
        // given: a classified member two levels down, below a getter each level re-checks
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);
        var tracker = new Tracker();

        // when
        _Logger(provider).LogInformation("Saw {Outer}", new Outer(new Inner(tracker, _Email)));

        // then
        tracker.Reads.Should().Be(1);
        sink.Entries.Should()
            .ContainSingle()
            .Which.Text.Should()
            .Be($"Saw Outer {{ Inner = Inner {{ Tracker = {typeof(Tracker)}, Email = ***** }} }}");
    }

    [Fact]
    public void should_stop_reading_values_past_the_budget_of_one_log_call()
    {
        // given: 100 branches of 100 values each, none classified, each value read through a getter
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);
        var tally = new Tally();
        List<object> branches = [];

        for (var i = 0; i < 100; i++)
        {
            List<object> leaves = [];

            for (var j = 0; j < 100; j++)
            {
                leaves.Add(new Counted(tally));
            }

            branches.Add(new Branch(leaves));
        }

        // when
        _Logger(provider).LogInformation("Saw {Tree}", new Branch(branches));

        // then: the walk gave up well before reading every value, and the unread part prints as type names
        tally.Reads.Should().BeLessThan(5_000);
        sink.Entries.Should().ContainSingle().Which.Text.Should().Contain(typeof(List<object>).ToString());
    }

    [Fact]
    public void should_cut_the_text_of_one_projection_at_the_budget()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);
        var body = new string('x', 40_000);

        // when
        _Logger(provider).LogInformation("Saw {Note}", new Note(_Email, body));

        // then
        var text = sink.Entries.Should().ContainSingle().Subject.Properties["Note"].Should().BeOfType<string>().Subject;
        text.Should().HaveLength((32 * 1024) + "...".Length);
        text.Should().StartWith("Note { Email = *****, Body = xxx").And.EndWith("x...");
    }

    [Fact]
    public void should_let_an_out_of_memory_failure_through()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);
        var logger = _Logger(provider);

        // when
        var log = () => logger.LogInformation("Saw {Batch}", new Batch(new StarvedCollection()));

        // then
        log.Should().Throw<OutOfMemoryException>();
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
    public void should_read_a_collection_once_for_both_detection_and_projection()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);
        var projection = new ClassifiedObjectProjection(provider.GetRequiredService<IRedactorProvider>());
        var customers = new CountingCollection<Customer>([new(_Email, "gold"), new("bob@example.test", "silver")]);

        // when
        var projected = projection.TryProject(customers, captured: false, out var text);

        // then
        projected.Should().BeTrue();
        text.Should().Be("Customer { Email = *****, Tier = gold }, Customer { Email = *****, Tier = silver }");
        customers.Enumerations.Should().Be(1);
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
    public void should_print_the_members_of_a_captured_class_without_classified_members()
    {
        // given: a class whose ToString prints only its name, holding another such class
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);
        var shipment = new Shipment
        {
            Id = 7,
            Carrier = new Carrier { Name = "acme" },
        };

        // when
        _Logger(provider).LogInformation("Sent {@Shipment}", shipment);

        // then
        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry.Text.Should().Be("Sent Shipment { Id = 7, Carrier = Carrier { Name = acme } }");
        entry.Properties["@Shipment"].Should().Be("Shipment { Id = 7, Carrier = Carrier { Name = acme } }");
    }

    [Fact]
    public void should_pass_a_class_without_classified_members_through_when_not_captured()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);
        var shipment = new Shipment
        {
            Id = 7,
            Carrier = new Carrier { Name = "acme" },
        };

        // when
        _Logger(provider).LogInformation("Sent {Shipment}", shipment);

        // then: without the capture prefix the provider prints the object as it would without redaction
        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry.Properties["Shipment"].Should().BeSameAs(shipment);
        entry.Text.Should().Be("Sent " + typeof(Shipment));
    }

    [Fact]
    public void should_keep_a_captured_type_that_writes_its_own_text()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);
        var money = new Money(10, "USD");

        // when
        _Logger(provider).LogInformation("Paid {@Amount}", money);

        // then: a type's own ToString is its chosen text, so it is not replaced by its members
        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry.Properties["@Amount"].Should().BeSameAs(money);
        entry.Text.Should().Be("Paid 10 USD");
    }

    [Fact]
    public void should_redact_each_element_of_a_classified_collection_member()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);

        // when
        _Logger(provider)
            .LogInformation("Saw {@Value}", new Mailing(["a@example.test", null, "b@example.test"], "weekly"));

        // then: each element is one redacted value, and a null element stays visible as null
        sink.Entries.Should()
            .ContainSingle()
            .Which.Text.Should()
            .Be("Saw Mailing { Recipients = [*****, (null), *****], Cadence = weekly }");
    }

    [Fact]
    public void should_print_a_null_element_beside_a_projected_one_in_a_logged_list()
    {
        // given
        using var sink = new CapturingProvider();
        using var provider = _Build(sink);
        List<Customer?> customers = [new(_Email, "gold"), null];

        // when
        _Logger(provider).LogInformation("Batch {Customers}", customers);

        // then
        sink.Entries.Should()
            .ContainSingle()
            .Which.Text.Should()
            .Be("Batch Customer { Email = *****, Tier = gold }, (null)");
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

    private static ServiceProvider _Build(
        CapturingProvider sink,
        Action<HeadlessRedactionSetupBuilder>? configure = null
    )
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders().AddProvider(sink));
        services.AddHeadlessRedaction(setup =>
        {
            setup.ConfigureRedaction(static redaction =>
                redaction.SetRedactor<StarRedactor>(new DataClassificationSet(HeadlessDataClassifications.Private))
            );
            configure?.Invoke(setup);
        });

        return services.BuildServiceProvider();
    }

    public sealed record Customer([property: PrivateData] string Email, string Tier);

    public sealed record Order(int Id, string State);

    public record Envelope(string Id, Customer Payload);

    public sealed record Box(object Value);

    public sealed class Shipment
    {
        public int Id { get; init; }

        public Carrier? Carrier { get; init; }
    }

    public sealed class Carrier
    {
        public string Name { get; init; } = "";
    }

    public sealed class Money(decimal amount, string currency)
    {
        public decimal Amount => amount;

        public string Currency => currency;

        public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{amount} {currency}");
    }

    public sealed record Mailing([property: PrivateData] List<string?> Recipients, string Cadence);

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

    public sealed record Profile([property: PrivateData] string Email, Exploding Badge);

    public sealed class Exploding
    {
#pragma warning disable CA1065 // The fixture is a broken type on purpose: the test proves its throwing ToString cannot fail a log call.
        public override string ToString() => throw new InvalidOperationException("The badge cannot be printed.");
#pragma warning restore CA1065
    }

    public sealed record Pending(Lazy<Customer> Next);

    // Shaped like an EF Core entity that takes the lazy-loading delegate through its constructor.
    public sealed class LazyOrder
    {
#pragma warning disable CA1823, RCS1213 // Never called: it marks the entity the way EF Core's constructor injection does.
        private readonly Action<object, string> _lazyLoader = static (_, _) => { };
#pragma warning restore CA1823, RCS1213

        public int Loads { get; private set; }

        public Customer Customer
        {
            get
            {
                Loads++;

                return new Customer(_Email, "gold");
            }
        }
    }

    public sealed record Outer(Inner Inner);

    public sealed record Inner(Tracker Tracker, [property: PrivateData] string Email);

    public sealed class Tracker
    {
        public int Reads { get; private set; }

        public object Value
        {
            get
            {
                Reads++;

                return "tracked";
            }
        }
    }

    public sealed record Branch(List<object> Children);

    public sealed class Tally
    {
        public int Reads { get; set; }
    }

    public sealed class Counted(Tally tally)
    {
        public object Value
        {
            get
            {
                tally.Reads++;

                return "leaf";
            }
        }
    }

    public sealed record Note([property: PrivateData] string Email, string Body);

    public sealed class StarvedCollection : IReadOnlyCollection<object>
    {
        public int Count => 1;

#pragma warning disable MA0012 // The test proves an out-of-memory failure is never swallowed, so it must raise one.
        public IEnumerator<object> GetEnumerator() => throw new OutOfMemoryException();
#pragma warning restore MA0012

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed record Store(IEnumerable<Customer> Rows);

    public sealed record Batch(IReadOnlyCollection<object> Rows);

    public sealed class PayloadException(Store payload) : InvalidOperationException("The save failed.")
    {
        public Store Payload { get; } = payload;
    }

    // A sequence that computes its elements on enumeration, the way a query does, and counts how often it ran.
    public sealed class CountingSequence<T>(IEnumerable<T> source) : IEnumerable<T>
    {
        public int Enumerations { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            Enumerations++;

            return source.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class CountingCollection<T>(IReadOnlyCollection<T> source) : IReadOnlyCollection<T>
    {
        public int Enumerations { get; private set; }

        public int Count => source.Count;

        public IEnumerator<T> GetEnumerator()
        {
            Enumerations++;

            return source.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // Throws the way an EF Core DbSet does for an entity type the model excludes.
    public sealed class ThrowingSequence : IEnumerable<Customer>
    {
        public IEnumerator<Customer> GetEnumerator() =>
            throw new InvalidOperationException("Cannot create a DbSet for 'Customer'.");

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class ThrowingCollection : IReadOnlyCollection<object>
    {
        public int Count => 1;

        public IEnumerator<object> GetEnumerator() =>
            throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
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

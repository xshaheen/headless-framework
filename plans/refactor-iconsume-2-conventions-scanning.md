# IConsume<T> Part 2: Conventions & Assembly Scanning

**Priority**: HIGH
**Dependencies**: Part 1 (Core Foundation) must be complete
**Estimated Effort**: 2 days

## Goal

Add convention-based topic naming and assembly scanning for automatic handler discovery.

## Scope

**In Scope:**
- Global conventions (kebab-case topics, prefixes, defaults)
- Message type → topic mapping
- Assembly scanning with filtering
- Convention-based auto-discovery

**Out of Scope:**
- Retry policies (Part 3)
- Filters (Part 4)
- Validation (Part 5)
- Advanced features (Part 6)

## Implementation

### Phase 1: Convention Abstractions

**Add to IMessagingBuilder.cs:**

```csharp
public interface IMessagingBuilder
{
    // Existing
    IMessagingBuilder AddConsumer<TConsumer>(Action<IConsumerConfigurator<TConsumer>>? configure = null);

    // NEW
    IMessagingBuilder ConfigureConventions(Action<IConventionConfigurator> configure);
    IMessagingBuilder MapTopic<TMessage>(string topic) where TMessage : class;
    IMessagingBuilder AddConsumersFromAssembly(Assembly assembly, Action<IAssemblyScanner>? configure = null);
}

public interface IConventionConfigurator
{
    void UseKebabCaseTopics();
    void UsePascalCaseTopics();
    void UseLowerCaseTopics();
    void TopicNamingConvention(Func<Type, string> convention);
    void TopicPrefix(string prefix);
    void TopicSuffix(string suffix);
    void DefaultGroup(string? group);
    void DefaultConcurrency(int count);
}

public interface IAssemblyScanner
{
    void Where(Func<Type, bool> predicate);
    void InNamespace(string @namespace);
    void ExcludeType<T>();
    void ExcludeType(Type type);
}
```

### Phase 2: Convention Implementation

**Create ConventionConfigurator.cs:**

```csharp
internal sealed class ConventionConfigurator : IConventionConfigurator
{
    private Func<Type, string>? _namingConvention;
    private string? _prefix;
    private string? _suffix;
    private string? _defaultGroup;
    private int _defaultConcurrency = 1;

    public void UseKebabCaseTopics() =>
        _namingConvention = type => type.Name.ToKebabCase();

    public void UsePascalCaseTopics() =>
        _namingConvention = type => type.Name;

    public void UseLowerCaseTopics() =>
        _namingConvention = type => type.Name.ToLowerInvariant();

    public void TopicNamingConvention(Func<Type, string> convention) =>
        _namingConvention = convention;

    public void TopicPrefix(string prefix) => _prefix = prefix;
    public void TopicSuffix(string suffix) => _suffix = suffix;
    public void DefaultGroup(string? group) => _defaultGroup = group;
    public void DefaultConcurrency(int count) => _defaultConcurrency = count;

    internal MessagingConventions Build()
    {
        return new MessagingConventions(
            _namingConvention ?? (type => type.Name),
            _prefix,
            _suffix,
            _defaultGroup,
            _defaultConcurrency
        );
    }
}

internal sealed record MessagingConventions(
    Func<Type, string> TopicNamingConvention,
    string? TopicPrefix,
    string? TopicSuffix,
    string? DefaultGroup,
    int DefaultConcurrency
)
{
    public string GetTopicName(Type messageType)
    {
        var baseName = TopicNamingConvention(messageType);
        var withPrefix = TopicPrefix != null ? $"{TopicPrefix}{baseName}" : baseName;
        var withSuffix = TopicSuffix != null ? $"{withPrefix}{TopicSuffix}" : withPrefix;
        return withSuffix;
    }
}
```

**Create StringExtensions.cs:**

```csharp
public static class StringExtensions
{
    public static string ToKebabCase(this string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var sb = new StringBuilder();
        sb.Append(char.ToLowerInvariant(text[0]));

        for (var i = 1; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsUpper(c))
            {
                sb.Append('-');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
```

### Phase 3: Assembly Scanner

**Create AssemblyScanner.cs:**

```csharp
internal sealed class AssemblyScanner : IAssemblyScanner
{
    private readonly List<Func<Type, bool>> _predicates = [];
    private readonly List<Type> _excludedTypes = [];
    private string? _namespace;

    public void Where(Func<Type, bool> predicate) => _predicates.Add(predicate);

    public void InNamespace(string @namespace) => _namespace = @namespace;

    public void ExcludeType<T>() => _excludedTypes.Add(typeof(T));

    public void ExcludeType(Type type) => _excludedTypes.Add(type);

    internal IEnumerable<Type> Scan(Assembly assembly)
    {
        var types = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType &&
                i.GetGenericTypeDefinition() == typeof(IConsume<>)));

        // Apply namespace filter
        if (_namespace != null)
        {
            types = types.Where(t => t.Namespace?.StartsWith(_namespace) == true);
        }

        // Apply custom predicates
        foreach (var predicate in _predicates)
        {
            types = types.Where(predicate);
        }

        // Exclude types
        types = types.Where(t => !_excludedTypes.Contains(t));

        return types;
    }
}
```

### Phase 4: Update MessagingBuilder

**Update MessagingBuilder.cs:**

```csharp
public sealed class MessagingBuilder(IServiceCollection services) : IMessagingBuilder
{
    private readonly ConsumerRegistry _registry = new();
    private readonly Dictionary<Type, string> _topicMappings = new();
    private MessagingConventions _conventions = new(
        type => type.Name,
        null, null, null, 1
    );

    public IMessagingBuilder ConfigureConventions(Action<IConventionConfigurator> configure)
    {
        var configurator = new ConventionConfigurator();
        configure(configurator);
        _conventions = configurator.Build();
        return this;
    }

    public IMessagingBuilder MapTopic<TMessage>(string topic) where TMessage : class
    {
        _topicMappings[typeof(TMessage)] = topic;
        return this;
    }

    public IMessagingBuilder AddConsumersFromAssembly(
        Assembly assembly,
        Action<IAssemblyScanner>? configure = null)
    {
        var scanner = new AssemblyScanner();
        configure?.Invoke(scanner);

        var consumerTypes = scanner.Scan(assembly);

        foreach (var consumerType in consumerTypes)
        {
            RegisterConsumer(consumerType, null);
        }

        return this;
    }

    public IMessagingBuilder AddConsumer<TConsumer>(
        Action<IConsumerConfigurator<TConsumer>>? configure = null)
        where TConsumer : class
    {
        var configurator = new ConsumerConfigurator<TConsumer>();
        configure?.Invoke(configurator);

        RegisterConsumer(typeof(TConsumer), configurator);
        return this;
    }

    private void RegisterConsumer(Type consumerType, ConsumerConfigurator? configurator)
    {
        // Find all IConsume<T> interfaces
        var consumeInterfaces = consumerType.GetInterfaces()
            .Where(i => i.IsGenericType &&
                       i.GetGenericTypeDefinition() == typeof(IConsume<>))
            .ToList();

        if (!consumeInterfaces.Any())
        {
            throw new InvalidOperationException(
                $"{consumerType.Name} does not implement IConsume<T>");
        }

        foreach (var consumeInterface in consumeInterfaces)
        {
            var messageType = consumeInterface.GetGenericArguments()[0];

            // Build metadata
            var metadata = configurator?.Build(messageType, consumerType)
                ?? BuildFromConventions(messageType, consumerType);

            _registry.Register(metadata);

            // Register in DI
            var serviceType = typeof(IConsume<>).MakeGenericType(messageType);
            services.AddScoped(serviceType, consumerType);
        }

        services.TryAddSingleton(_registry);
    }

    private ConsumerMetadata BuildFromConventions(Type messageType, Type consumerType)
    {
        // Check for explicit topic mapping
        var topic = _topicMappings.TryGetValue(messageType, out var mapped)
            ? mapped
            : _conventions.GetTopicName(messageType);

        return new ConsumerMetadata(
            messageType,
            consumerType,
            topic,
            _conventions.DefaultGroup,
            _conventions.DefaultConcurrency
        );
    }
}
```

## Testing

### Unit Tests

```csharp
public class ConventionsTest
{
    [Fact]
    public void should_use_kebab_case_topics()
    {
        var services = new ServiceCollection();
        services.AddMessaging(m =>
        {
            m.ConfigureConventions(c => c.UseKebabCaseTopics());
            m.AddConsumer<OrderCreatedHandler>();
        });

        var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<ConsumerRegistry>();
        var metadata = registry.GetByMessageType(typeof(OrderCreated));

        metadata!.Topic.Should().Be("order-created");
    }

    [Fact]
    public void should_apply_topic_prefix()
    {
        var services = new ServiceCollection();
        services.AddMessaging(m =>
        {
            m.ConfigureConventions(c =>
            {
                c.UseKebabCaseTopics();
                c.TopicPrefix("prod.");
            });
            m.AddConsumer<OrderCreatedHandler>();
        });

        var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<ConsumerRegistry>();
        var metadata = registry.GetByMessageType(typeof(OrderCreated));

        metadata!.Topic.Should().Be("prod.order-created");
    }

    [Fact]
    public void should_use_topic_mapping_over_convention()
    {
        var services = new ServiceCollection();
        services.AddMessaging(m =>
        {
            m.ConfigureConventions(c => c.UseKebabCaseTopics());
            m.MapTopic<OrderCreated>("orders.created");
            m.AddConsumer<OrderCreatedHandler>();
        });

        var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<ConsumerRegistry>();
        var metadata = registry.GetByMessageType(typeof(OrderCreated));

        metadata!.Topic.Should().Be("orders.created");
    }

    [Fact]
    public void should_apply_default_group_and_concurrency()
    {
        var services = new ServiceCollection();
        services.AddMessaging(m =>
        {
            m.ConfigureConventions(c =>
            {
                c.DefaultGroup("my-service");
                c.DefaultConcurrency(5);
            });
            m.AddConsumer<OrderCreatedHandler>();
        });

        var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<ConsumerRegistry>();
        var metadata = registry.GetByMessageType(typeof(OrderCreated));

        metadata!.Group.Should().Be("my-service");
        metadata.Concurrency.Should().Be(5);
    }
}

public class AssemblyScanningTest
{
    [Fact]
    public void should_discover_all_consumers_in_assembly()
    {
        var services = new ServiceCollection();
        services.AddMessaging(m =>
        {
            m.AddConsumersFromAssembly(Assembly.GetExecutingAssembly());
        });

        var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<ConsumerRegistry>();
        var all = registry.GetAll();

        all.Should().Contain(m => m.MessageType == typeof(OrderCreated));
        all.Should().Contain(m => m.MessageType == typeof(OrderShipped));
    }

    [Fact]
    public void should_filter_by_namespace()
    {
        var services = new ServiceCollection();
        services.AddMessaging(m =>
        {
            m.AddConsumersFromAssembly(Assembly.GetExecutingAssembly(), scan =>
            {
                scan.InNamespace("MyApp.Handlers");
            });
        });

        var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<ConsumerRegistry>();
        var all = registry.GetAll();

        all.Should().AllSatisfy(m =>
            m.ConsumerType.Namespace.Should().StartWith("MyApp.Handlers"));
    }

    [Fact]
    public void should_exclude_types()
    {
        var services = new ServiceCollection();
        services.AddMessaging(m =>
        {
            m.AddConsumersFromAssembly(Assembly.GetExecutingAssembly(), scan =>
            {
                scan.ExcludeType<LegacyHandler>();
            });
        });

        var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<ConsumerRegistry>();
        var all = registry.GetAll();

        all.Should().NotContain(m => m.ConsumerType == typeof(LegacyHandler));
    }

    [Fact]
    public void should_register_multi_message_handlers()
    {
        var services = new ServiceCollection();
        services.AddMessaging(m =>
        {
            m.AddConsumersFromAssembly(Assembly.GetExecutingAssembly());
        });

        var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<ConsumerRegistry>();
        var all = registry.GetAll();

        // OrderEventHandler implements IConsume<OrderCreated> and IConsume<OrderShipped>
        all.Count(m => m.ConsumerType == typeof(OrderEventHandler)).Should().Be(2);
    }
}

public class StringExtensionsTest
{
    [Theory]
    [InlineData("OrderCreated", "order-created")]
    [InlineData("PaymentReceived", "payment-received")]
    [InlineData("HTTPRequest", "h-t-t-p-request")]
    [InlineData("order", "order")]
    public void should_convert_to_kebab_case(string input, string expected)
    {
        input.ToKebabCase().Should().Be(expected);
    }
}
```

## Acceptance Criteria

- [ ] `ConfigureConventions()` sets global defaults
- [ ] Kebab-case, Pascal-case, lowercase conventions work
- [ ] Custom naming conventions work
- [ ] Topic prefix/suffix applied correctly
- [ ] Default group/concurrency applied
- [ ] `MapTopic<T>()` overrides conventions
- [ ] `AddConsumersFromAssembly()` discovers handlers
- [ ] Namespace filtering works
- [ ] Type exclusion works
- [ ] Multi-message handlers registered for each interface
- [ ] Unit tests pass
- [ ] Coverage ≥85%

## Usage Example

```csharp
services.AddMessaging(messaging =>
{
    // Global conventions
    messaging.ConfigureConventions(c =>
    {
        c.UseKebabCaseTopics();  // OrderCreated → order-created
        c.TopicPrefix("prod.");
        c.DefaultGroup("order-service");
        c.DefaultConcurrency(3);
    });

    // Topic mappings (override conventions)
    messaging.MapTopic<OrderCreated>("orders.created");
    messaging.MapTopic<PaymentReceived>("payments.received");

    // Scan assembly
    messaging.AddConsumersFromAssembly(typeof(Program).Assembly, scan =>
    {
        scan.InNamespace("OrderService.Handlers");
        scan.ExcludeType<DeprecatedHandler>();
    });

    // Override specific handler
    messaging.AddConsumer<CriticalOrderHandler>(c =>
    {
        c.Topic("orders.critical");
        c.Concurrency(1);
    });
});
```

## Files Changed

**Created:**
- `src/Framework.Messages.Abstractions/IConventionConfigurator.cs`
- `src/Framework.Messages.Abstractions/IAssemblyScanner.cs`
- `src/Framework.Messages.Core/ConventionConfigurator.cs`
- `src/Framework.Messages.Core/MessagingConventions.cs`
- `src/Framework.Messages.Core/AssemblyScanner.cs`
- `src/Framework.Messages.Core/StringExtensions.cs`
- `tests/Framework.Messages.Core.Tests.Unit/ConventionsTest.cs`
- `tests/Framework.Messages.Core.Tests.Unit/AssemblyScanningTest.cs`
- `tests/Framework.Messages.Core.Tests.Unit/StringExtensionsTest.cs`

**Modified:**
- `src/Framework.Messages.Abstractions/IMessagingBuilder.cs`
- `src/Framework.Messages.Core/MessagingBuilder.cs`

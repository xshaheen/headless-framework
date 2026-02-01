# Headless.Messaging.Core.Tests.Harness

Shared test harness for messaging providers. Provides base test classes with reusable test scenarios for transport, consumer, and storage implementations.

## Overview

This harness enables consistent integration testing across all messaging providers (RabbitMQ, Kafka, NATS, AWS SQS, etc.) by defining standard test scenarios that each provider can run against its real infrastructure.

## Base Test Classes

| Class | Purpose |
|-------|---------|
| `TransportTestsBase` | Tests for `ITransport` implementations (sending messages) |
| `ConsumerClientTestsBase` | Tests for `IConsumerClient` implementations (receiving messages) |
| `DataStorageTestsBase` | Tests for `IDataStorage` implementations (message persistence) |
| `MessagingIntegrationTestsBase` | Full pub-sub cycle tests with DI setup |

## Capability Flags

Each base class uses capability flags to skip tests that don't apply to a specific provider.

### TransportCapabilities

```csharp
protected override TransportCapabilities Capabilities => new()
{
    SupportsOrdering = true,      // Messages maintain order (e.g., RabbitMQ, Kafka)
    SupportsDeadLetter = true,    // Dead letter queue support
    SupportsPriority = false,     // Message priority levels
    SupportsDelayedDelivery = false, // Scheduled/delayed messages
    SupportsBatchSend = true,     // Batch message publishing (default: true)
    SupportsHeaders = true,       // Custom message headers (default: true)
};
```

### ConsumerClientCapabilities

```csharp
protected override ConsumerClientCapabilities Capabilities => new()
{
    SupportsFetchTopics = true,        // Can fetch topic metadata (default: true)
    SupportsConcurrentProcessing = true, // Concurrent message handling (default: true)
    SupportsReject = true,             // Reject/nack messages (default: true)
    SupportsGracefulShutdown = true,   // Clean shutdown support (default: true)
};
```

### DataStorageCapabilities

```csharp
protected override DataStorageCapabilities Capabilities => new()
{
    SupportsLocking = true,           // Distributed locking (default: true)
    SupportsExpiration = true,        // Message TTL/expiration (default: true)
    SupportsConcurrentOperations = true, // Concurrent storage ops (default: true)
    SupportsDelayedScheduling = true, // Delayed message scheduling (default: true)
    SupportsMonitoringApi = true,     // Monitoring/stats API (default: true)
};
```

## Creating Transport Provider Tests

### 1. Create the Integration Test Project

Create `tests/Headless.Messaging.<Provider>.Tests.Integration/`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Tests</RootNamespace>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="Testcontainers.<Provider>" />
    <PackageReference Include="Testcontainers.XunitV3" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Headless.Messaging.<Provider>\Headless.Messaging.<Provider>.csproj" />
    <ProjectReference Include="..\Headless.Messaging.Core.Tests.Harness\Headless.Messaging.Core.Tests.Harness.csproj" />
  </ItemGroup>
</Project>
```

### 2. Create the Container Fixture

```csharp
using Testcontainers.<Provider>;
using Testcontainers.Xunit;
using Xunit.Sdk;

namespace Tests;

[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class <Provider>Fixture(IMessageSink messageSink)
    : ContainerFixture<<Provider>Builder, <Provider>Container>(messageSink),
        ICollectionFixture<<Provider>Fixture>
{
    public string ConnectionString => Container.GetConnectionString();

    protected override <Provider>Builder Configure()
    {
        return base.Configure().WithImage("<provider>:<version>");
    }
}
```

### 3. Create Transport Tests

```csharp
using Headless.Messaging.Transport;
using Tests.Capabilities;

namespace Tests;

[Collection<<Provider>Fixture>]
public sealed class <Provider>TransportTests(<Provider>Fixture fixture) : TransportTestsBase
{
    protected override TransportCapabilities Capabilities => new()
    {
        SupportsOrdering = true,
        SupportsDeadLetter = true,
        // Set flags based on provider capabilities
    };

    protected override ITransport GetTransport()
    {
        // Create and return configured transport using fixture.ConnectionString
    }

    // Expose base tests as [Fact] methods
    [Fact]
    public override Task should_send_message_successfully() => base.should_send_message_successfully();

    [Fact]
    public override Task should_have_valid_broker_address() => base.should_have_valid_broker_address();

    [Fact]
    public override Task should_include_headers_in_sent_message() => base.should_include_headers_in_sent_message();

    // ... expose all relevant base tests
}
```

## Creating Storage Provider Tests

### 1. Create Storage Tests

```csharp
using Headless.Messaging.Persistence;
using Tests.Capabilities;

namespace Tests;

[Collection<PostgreSqlFixture>]
public sealed class PostgreSqlStorageTests(PostgreSqlFixture fixture) : DataStorageTestsBase
{
    protected override DataStorageCapabilities Capabilities => new()
    {
        SupportsLocking = true,
        SupportsExpiration = true,
        SupportsDelayedScheduling = true,
        SupportsMonitoringApi = true,
    };

    protected override IDataStorage GetStorage()
    {
        // Create configured storage instance
    }

    protected override IStorageInitializer GetInitializer()
    {
        // Create storage initializer
    }

    // Expose base tests
    [Fact]
    public override Task should_initialize_schema() => base.should_initialize_schema();

    [Fact]
    public override Task should_store_published_message() => base.should_store_published_message();

    // ... expose all relevant base tests
}
```

## Full Integration Tests

For complete pub-sub cycle tests with DI:

```csharp
using Headless.Messaging.Configuration;

namespace Tests;

[Collection<RabbitMqFixture>]
public sealed class RabbitMqIntegrationTests(RabbitMqFixture fixture)
    : MessagingIntegrationTestsBase
{
    protected override void ConfigureTransport(MessagingOptions options)
    {
        options.UseRabbitMQ(r =>
        {
            r.HostName = fixture.HostName;
            r.Port = fixture.Port;
            r.UserName = fixture.UserName;
            r.Password = fixture.Password;
        });
    }

    protected override void ConfigureStorage(MessagingOptions options)
    {
        options.UseInMemoryStorage(); // Or a real storage provider
    }

    [Fact]
    public override Task should_publish_and_consume_message_end_to_end()
        => base.should_publish_and_consume_message_end_to_end();

    [Fact]
    public override Task should_discover_consumers_from_di()
        => base.should_discover_consumers_from_di();

    // ... expose all relevant base tests
}
```

## Test Helpers

### TestMessage

Simple record for testing message serialization:

```csharp
public sealed record TestMessage
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Payload { get; init; }
}
```

### TestSubscriber

Collects received messages for assertions:

```csharp
var subscriber = ServiceProvider.GetRequiredService<TestSubscriber>();
await Publisher.PublishAsync("topic", message);
var received = await subscriber.WaitForMessageAsync(TimeSpan.FromSeconds(10));
received.Should().BeTrue();
subscriber.ReceivedMessages.Should().ContainSingle(m => m.Id == message.Id);
```

## Running Tests

```bash
# Run specific integration tests
dotnet test tests/Headless.Messaging.RabbitMq.Tests.Integration

# Run with filter
dotnet test --filter "FullyQualifiedName~RabbitMqTransportTests"
```

## Adding New Test Scenarios

1. Add virtual test method to appropriate base class
2. Use capability flags for conditional execution
3. Existing provider tests automatically gain new scenarios

```csharp
// In TransportTestsBase
public virtual async Task should_handle_new_scenario()
{
    if (!Capabilities.SupportsNewFeature)
    {
        Assert.Skip("Transport does not support new feature");
    }
    // Test implementation
}
```

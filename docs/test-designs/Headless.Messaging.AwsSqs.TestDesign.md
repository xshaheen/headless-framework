# Test Case Design: Headless.Messaging.AwsSqs

**Package:** `src/Headless.Messaging.AwsSqs`
**Test Projects:** `Headless.Messaging.AwsSqs.Tests.Unit` ✅, `Headless.Messaging.AwsSqs.Tests.Integration` ✅
**Generated:** 2026-01-25

## Package Analysis

AWS SQS transport with FIFO queue support.

| File | Type | Priority |
|------|------|----------|
| `AmazonSqsTransport.cs` | Transport impl | P1 |
| `AmazonSqsConsumerClient.cs` | Consumer client | P1 (CRITICAL BUGS) |
| `AmazonSqsConsumerClientFactory.cs` | Factory | P2 |
| `MessagingSqsOptions.cs` | Options | P2 |
| `SqsHeader.cs` | Header encoding | P2 |
| `Setup.cs` | DI registration | P3 |

## Known Issues (CRITICAL)

1. **Double semaphore release** (todo #001) - Concurrency limit bypass
2. **Single message fetch** (todo #009) - 10x less efficient

## Test Recommendation

### Additional Unit Tests Needed

#### AmazonSqsConsumerClient Tests (CRITICAL)
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_not_double_release_semaphore_on_exception` | P1 | **BUG: Verify fix** |
| `should_fetch_batch_of_10_messages` | P1 | **BUG: Currently 1** |
| `should_invoke_callback_for_each_message_in_batch` | P1 | Batch processing |
| `should_delete_message_on_commit` | P1 | Delete from queue |
| `should_change_visibility_on_reject` | P1 | Message retry |
| `should_respect_semaphore_concurrency` | P1 | Concurrency limit |
| `should_use_long_polling` | P2 | Wait time |
| `should_handle_empty_response` | P2 | No messages |
| `should_stop_on_cancellation` | P2 | Graceful stop |

#### AmazonSqsTransport Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_send_message_to_queue` | P1 | Basic send |
| `should_include_message_attributes` | P1 | Header encoding |
| `should_set_message_group_id_for_fifo` | P1 | FIFO support |
| `should_set_deduplication_id_for_fifo` | P1 | Deduplication |
| `should_handle_send_failure` | P1 | Error handling |

#### MessagingSqsOptions Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_require_region` | P2 | Required config |
| `should_support_local_stack_endpoint` | P2 | Testing config |
| `should_detect_fifo_queues` | P2 | .fifo suffix |

### Integration Tests

Review existing tests in `AwsSqs.Tests.Integration` for coverage of bug scenarios.

| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_process_batch_concurrently` | P1 | Batch + concurrency |
| `should_handle_visibility_timeout` | P2 | Message retry |
| `should_work_with_fifo_queues` | P2 | FIFO semantics |

## Critical Bug Tests

```csharp
[Fact]
public async Task semaphore_should_not_be_released_twice_on_exception()
{
    // Arrange
    var options = new MessagingSqsOptions { /* ... */ };
    var client = new AmazonSqsConsumerClient(/* ... */);
    var initialCount = GetSemaphoreCount(client);

    // Act - simulate exception during processing
    // This should release semaphore exactly once, not twice

    // Assert
    var finalCount = GetSemaphoreCount(client);
    finalCount.Should().Be(initialCount); // Not > initialCount
}

[Fact]
public async Task should_fetch_10_messages_per_request()
{
    // Arrange
    var mockSqs = Substitute.For<IAmazonSQS>();

    // Act
    await client.FetchMessages();

    // Assert
    await mockSqs.Received().ReceiveMessageAsync(
        Arg.Is<ReceiveMessageRequest>(r => r.MaxNumberOfMessages == 10),
        Arg.Any<CancellationToken>()
    );
}
```

## Test Infrastructure

```csharp
// Use LocalStack for SQS
public class SqsFixture : IAsyncLifetime
{
    private readonly LocalStackContainer _container = new LocalStackBuilder()
        .WithImage("localstack/localstack:3.0")
        .WithServices(LocalStackService.Sqs)
        .Build();

    public string ServiceUrl => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
```

## Summary

| Metric | Value |
|--------|-------|
| Source Files | 6 |
| Existing Unit Tests | Some ✅ |
| Existing Integration Tests | Some ✅ |
| **Additional Unit Tests Needed** | **~15 cases** |
| Priority | **P1 CRITICAL** - Concurrency bug |

**CRITICAL:** The double semaphore release bug can cause the concurrency limit to be bypassed, potentially overwhelming the system.

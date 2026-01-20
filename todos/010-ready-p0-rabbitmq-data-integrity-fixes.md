# RabbitMQ Data Integrity Critical Fixes

**Priority:** P0  
**Status:** Ready  
**Component:** Framework.Messages.RabbitMQ  
**Review Document:** /data-integrity-review-rabbitmq.md

## Context

Data integrity review identified critical message loss scenarios in RabbitMQ transport:
- Missing DLX configuration despite README claims
- Semaphore release timing allows ack/nack loss
- Publisher confirms configured but not implemented
- Channel closure race conditions

## Critical Issues to Fix

### 1. Implement Dead Letter Exchange (CRITICAL)
**File:** `src/Framework.Messages.RabbitMQ/RabbitMQConsumerClient.cs:127-149`

**Problem:** Rejected messages requeue infinitely then disappear at TTL expiry with no audit trail.

**Fix:**
```csharp
// In ConnectAsync method, add to queue arguments:
arguments.Add("x-dead-letter-exchange", $"{_exchangeName}.dlx");
arguments.Add("x-dead-letter-routing-key", $"{groupName}.failed");

// Before queue declaration, add:
await _channel.ExchangeDeclareAsync($"{_exchangeName}.dlx", "topic", true);
await _channel.QueueDeclareAsync(
    $"{groupName}.failed",
    durable: true,
    exclusive: false,
    autoDelete: false,
    arguments: new Dictionary<string, object?>()
);
await _channel.QueueBindAsync($"{groupName}.failed", $"{_exchangeName}.dlx", "#");
```

**Also:** Update README.md to accurately reflect DLX configuration, or remove claim if not implementing.

### 2. Fix Semaphore Release Timing (CRITICAL)
**File:** `src/Framework.Messages.RabbitMQ/RabbitMQBasicConsumer.cs:105-123`

**Problem:** Semaphore released after ack/nack. If channel closes mid-operation, semaphore leaks and ack is lost.

**Fix:**
```csharp
public async Task BasicAck(ulong deliveryTag)
{
    try
    {
        if (!Channel.IsOpen)
        {
            var ex = new InvalidOperationException($"Cannot ack {deliveryTag}: channel closed");
            logCallback(new LogMessageEventArgs 
            { 
                LogType = MqLogType.AcknowledgmentFailed, 
                Reason = ex.Message 
            });
            throw ex;
        }
        
        await Channel.BasicAckAsync(deliveryTag, false);
    }
    finally
    {
        _semaphore.Release();  // Always release
    }
}

public async Task BasicReject(ulong deliveryTag)
{
    try
    {
        if (!Channel.IsOpen)
        {
            var ex = new InvalidOperationException($"Cannot reject {deliveryTag}: channel closed");
            logCallback(new LogMessageEventArgs 
            { 
                LogType = MqLogType.AcknowledgmentFailed, 
                Reason = ex.Message 
            });
            throw ex;
        }
        
        await Channel.BasicRejectAsync(deliveryTag, true);
    }
    finally
    {
        _semaphore.Release();  // Always release
    }
}
```

### 3. Implement Publisher Confirms (HIGH)
**File:** `src/Framework.Messages.RabbitMQ/RabbitMqTransport.cs:27-82`

**Problem:** `PublishConfirms` option exists but no confirmation wait. Network failures silently lose messages.

**Fix:**
```csharp
public async Task<OperateResult> SendAsync(TransportMessage message)
{
    IChannel? channel = null;
    try
    {
        channel = await _connectionChannelPool.Rent();

        var props = new BasicProperties
        {
            MessageId = message.GetId(),
            DeliveryMode = DeliveryModes.Persistent,
            Headers = message.Headers.ToDictionary(x => x.Key, object? (x) => x.Value, StringComparer.Ordinal),
        };

        await channel.BasicPublishAsync(_exchange, message.GetName(), false, props, message.Body);

        if (_connectionChannelPool.IsPublishConfirmsEnabled)  // Add this property
        {
            if (!await channel.WaitForConfirmsAsync(TimeSpan.FromSeconds(5)))
            {
                throw new PublisherSentFailedException("Publish not confirmed by broker within timeout");
            }
        }

        _logger.LogInformation(
            "Message '{Name}' published, internal id '{Id}'",
            message.GetName(),
            message.GetId()
        );

        return OperateResult.Success;
    }
    catch (Exception ex)
    {
        channel = null;  // Don't return to pool on error
        
        if (ex is AlreadyClosedException && channel?.IsOpen == true)
        {
            await channel.DisposeAsync();
        }

        var wrapperEx = new PublisherSentFailedException(ex.Message, ex);
        var errors = new OperateError
        {
            Code = ex.HResult.ToString(CultureInfo.InvariantCulture),
            Description = ex.Message,
        };

        return OperateResult.Failed(wrapperEx, errors);
    }
    finally
    {
        if (channel?.IsOpen == true)
        {
            _connectionChannelPool.Return(channel);
        }
        else
        {
            channel?.Dispose();
        }
    }
}
```

**Also:** Add `IsPublishConfirmsEnabled` property to `IConnectionChannelPool`.

### 4. Add Requeue Limits (MEDIUM)
**File:** `src/Framework.Messages.RabbitMQ/RabbitMQBasicConsumer.cs:115-123`

**Problem:** Poison messages requeue forever, blocking queue.

**Fix:**
```csharp
// In _Consume method, extract redelivery count:
private Task _Consume(
    string consumerTag,
    ulong deliveryTag,
    bool redelivered,
    string exchange,
    string routingKey,
    IReadOnlyBasicProperties properties,
    ReadOnlyMemory<byte> body
)
{
    var headers = new Dictionary<string, string?>(StringComparer.Ordinal);

    // Extract x-death header for redelivery count
    int redeliveryCount = 0;
    if (properties.Headers != null && 
        properties.Headers.TryGetValue("x-death", out var xDeath) && 
        xDeath is List<object> deaths && 
        deaths.Count > 0 &&
        deaths[0] is Dictionary<string, object> death &&
        death.TryGetValue("count", out var count))
    {
        redeliveryCount = Convert.ToInt32(count);
    }

    headers["x-redelivery-count"] = redeliveryCount.ToString();
    
    // Rest of existing code...
    
    return msgCallback(message, (deliveryTag, redeliveryCount));  // Pass both
}

// Update BasicReject signature:
public async Task BasicReject(ulong deliveryTag, int redeliveryCount)
{
    const int MaxRetries = 3;
    
    try
    {
        if (!Channel.IsOpen)
        {
            throw new InvalidOperationException($"Cannot reject {deliveryTag}: channel closed");
        }
        
        bool requeue = redeliveryCount < MaxRetries;
        await Channel.BasicRejectAsync(deliveryTag, requeue);
        
        if (!requeue)
        {
            logCallback(new LogMessageEventArgs 
            { 
                LogType = MqLogType.MessageMovedToDLQ, 
                Reason = $"Max retries ({MaxRetries}) exceeded for deliveryTag {deliveryTag}" 
            });
        }
    }
    finally
    {
        _semaphore.Release();
    }
}
```

**Also:** Update `IConsumerClient.RejectAsync` signature to accept sender tuple: `(ulong deliveryTag, int redeliveryCount)`.

### 5. Observe Task.Run Exceptions (MEDIUM)
**File:** `src/Framework.Messages.RabbitMQ/RabbitMQBasicConsumer.cs:35-50`

**Problem:** Fire-and-forget pattern loses exceptions, causing silent message loss.

**Fix:**
```csharp
public override async Task HandleBasicDeliverAsync(
    string consumerTag,
    ulong deliveryTag,
    bool redelivered,
    string exchange,
    string routingKey,
    IReadOnlyBasicProperties properties,
    ReadOnlyMemory<byte> body,
    CancellationToken cancellationToken = default
)
{
    if (_usingTaskRun)
    {
        await _semaphore.WaitAsync(cancellationToken);
        ReadOnlyMemory<byte> safeBody = body.ToArray();
        
        var consumeTask = Task.Run(
            () => _Consume(consumerTag, deliveryTag, redelivered, exchange, routingKey, properties, safeBody),
            cancellationToken
        );

        // Observe task completion
        _ = consumeTask.ContinueWith(
            task =>
            {
                if (task.IsFaulted)
                {
                    logCallback(new LogMessageEventArgs
                    {
                        LogType = MqLogType.ConsumeTaskFaulted,
                        Reason = task.Exception?.ToString() ?? "Unknown error"
                    });
                    
                    // Extract redelivery count from properties (same logic as in _Consume)
                    int redeliveryCount = 0;
                    // ... extraction logic
                    
                    // Reject message
                    _ = BasicReject(deliveryTag, redeliveryCount);
                }
            },
            TaskScheduler.Default
        );
    }
    else
    {
        await _Consume(consumerTag, deliveryTag, redelivered, exchange, routingKey, properties, body)
            .AnyContext();
    }
}
```

## Testing Requirements

### Unit Tests
- [ ] Semaphore released even when ack/nack throws
- [ ] Exception thrown when acking on closed channel
- [ ] Requeue=false after max retries
- [ ] Publisher confirms timeout throws exception

### Integration Tests (Testcontainers)
- [ ] Message moves to DLQ after max retries
- [ ] DLQ messages can be inspected
- [ ] Channel closure during ack → message redelivered
- [ ] Publisher confirms: kill broker mid-send → error returned
- [ ] Poison message doesn't block queue
- [ ] Faulted Task.Run triggers reject

## Definition of Done

- [ ] All 5 critical issues fixed
- [ ] Unit tests pass with >85% coverage
- [ ] Integration tests pass (requires RabbitMQ via Testcontainers)
- [ ] README.md accurately reflects DLX behavior
- [ ] Add MqLogType enum entries: AcknowledgmentFailed, MessageMovedToDLQ, ConsumeTaskFaulted
- [ ] Manual testing with chaos scenarios (kill broker, network delay, high load)

## Estimated Effort

- DLX implementation: 4 hours
- Semaphore fixes: 2 hours
- Publisher confirms: 3 hours
- Requeue limits: 2 hours
- Task.Run observability: 2 hours
- Testing: 8 hours
- **Total: 21 hours (~3 days)**

## References

- Review doc: `/data-integrity-review-rabbitmq.md`
- RabbitMQ DLX docs: https://www.rabbitmq.com/docs/dlx
- Publisher confirms: https://www.rabbitmq.com/docs/confirms
- x-death header: https://www.rabbitmq.com/docs/dlx#message-properties

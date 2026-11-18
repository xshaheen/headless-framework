---
domain: Messaging
packages: Messaging.Aws
---

# AWS SNS/SQS transport

> Part of [Messaging](../messaging.md). The `Headless.Messaging.Aws` transport: SNS topics for the Bus lane, SQS queues for the Queue lane, FIFO support, and the IAM actions each workload needs.

## Headless.Messaging.Aws

Use this transport for AWS-native pub/sub and queue workloads. Bus publishes go to SNS topics fanned out to one SQS queue per consumer identity. Queue sends go straight to SQS.

### Setup

```bash
dotnet add package Headless.Messaging.Aws
```

```csharp
setup.UseAws(options =>
{
    options.Region = Amazon.RegionEndpoint.USEast1;
});

services.ConfigureMessaging(messaging =>
    messaging
        .Message<PlaceOrder>("orders-place.fifo")
        .OnQueue(queue => queue.UseAws(aws => aws.MessageGroupId(order => order.CustomerId.ToString())))
);

[QueueConsumer("orders.place-order")]
public sealed class PlaceOrderWorker : IConsume<PlaceOrder>
{
    public ValueTask ConsumeAsync(ConsumeContext<PlaceOrder> context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}
```

**Verify:** the `messaging-broker` readiness check reports `Healthy` after the first receive or send ([Broker reachability](operations.md#broker-reachability)), and the `describe` host manifest's `messaging.transports` shows `"bus": "aws.sns"` and `"queue": "aws_sqs"`. A denied consumer topology call fails with `AWS_MESSAGING_PROVISIONING_DENIED` naming the missing stage.

### Names and topology

- Bus publishes to the SNS topic `bus-{logical-name}`. Each Bus consumer identity owns the SQS queue `bus-{consumer-identity}`, subscribed to that topic. Queue sends go to `queue-{logical-name}`. In names, `.` becomes `-` and `:` becomes `_`.
- Bus and Queue have independent topology, so one contract name can carry both lanes without cross-delivery.
- `AutoProvision = true` (default) creates SNS topics, SQS queues, the queue access policy, and raw-delivery SNS-to-SQS subscriptions on first use.
- `AutoProvision = false` looks topics and queues up and creates nothing, for topology managed by infrastructure as code. Every topic, queue, and subscription must then exist before the host starts, and each Bus subscription must enable raw message delivery. A missing queue fails consumer startup naming the queue lookup. A missing topic fails the send. After a miss, sends to that topic fail without a new `sns:ListTopics` lookup for 30 seconds, so a topic provisioned while the host runs is found within that window.
- Consumer startup honors host cancellation through SNS/SQS provisioning and subscription.
- Not supported: every-instance consumers (startup fails naming the consumer) and request/reply (a host that sends requests or declares a responder fails startup).

### Configuration

Configure region, service URLs, and credentials through `AmazonSqsMessagingOptions` (or the AWS SDK default credential chain). Its other settings:

| Option | Default | Effect |
| --- | --- | --- |
| `AutoProvision` | `true` | Creates topology on first use. With `false`, queues are looked up with `sqs:GetQueueUrl` and topics with `sns:ListTopics`, and nothing is created, subscribed, or given a policy |
| `VisibilityTimeout` | 30 s | Sent on every receive, so the queue's own default does not apply. Whole seconds from 1 second to 12 hours |
| `ReceiveWaitTime` | 5 s | SQS long-poll wait per receive. Whole seconds from 0 to 20 |

**FIFO and routing affinity.** `RoutingAffinityKey` maps to the native `MessageGroupId` only for registered `.fifo` SNS topics or SQS queues. Keys are 1 to 128 printable ASCII characters (`!` to `~`), without spaces. `MessageGroupId(...)` on the lane builder and the `AwsMessagingHeaders.MessageGroupId` header are raw adapters. They must agree with a typed key when you set both. `MessageGroupId(...)` is message-side only, because it is stamped at publish, and a value longer than 128 characters is rejected. On a standard (non-FIFO) route, a typed key travels in the header bag only, without those length and character limits, and gives no affinity guarantee.

**FIFO consumption order.** On a FIFO queue the consumer reads each message's `MessageGroupId`, takes a group's messages in one at a time in receive order, and returns the rest of a group's batch to the queue as soon as one of them is rejected, hidden as long as the rejected message. Admission to the inbox therefore keeps the group's order. Handlers run after admission, so a shared group does not make handling exclusive on its own. Tune the consumer `Sequential()` with the group as its `RoutingAffinityKey`, or run it at `Concurrency(1)`. See [Transport-Specific Ordering](ordering.md#transport-specific-ordering).

**Envelope format.** Every send puts the whole header dictionary into one String attribute named `headless-aws-headers-v1`: an SQS message attribute on the Queue lane and an SNS message attribute on the Bus lane. The body and the native `MessageGroupId` are unchanged. Bus subscriptions use SNS raw message delivery (`RawMessageDelivery = true`), so SQS receives the body and attribute as sent. With `AutoProvision`, the consumer sets raw delivery on a subscription it creates and on one that already exists (`sns:SetSubscriptionAttributes`). One attribute stays inside the SQS limit of ten message attributes. A message whose attribute is missing (including an SNS-wrapped body from a subscription without raw delivery), mixed with other attributes, malformed, or holding duplicate or reserved header names is deleted from the queue without reaching a handler.

### AWS permissions

`AmazonSqsMessagingOptions.Credentials`, or the SDK default chain, supplies one identity for runtime and topology calls. There is no separate provisioning credential. Grant only the rows each workload uses:

| Workload | Runtime actions | Provisioning and discovery actions | Resource scope |
| --- | --- | --- | --- |
| Bus publisher | `sns:ListTopics`, `sns:Publish` | `sns:CreateTopic` when a `bus-*` topic is absent | `sns:ListTopics` requires `Resource: "*"`; scope create and publish to `arn:${Partition}:sns:${Region}:${Account}:bus-*` |
| Queue publisher | `sqs:SendMessage` | `sqs:CreateQueue` on first use in each process, also for a pre-created queue, because the idempotent create call resolves its URL | `arn:${Partition}:sqs:${Region}:${Account}:queue-*` |
| Bus consumer | `sqs:ReceiveMessage`, `sqs:DeleteMessage`, `sqs:ChangeMessageVisibility` | `sns:CreateTopic`, `sqs:CreateQueue`, `sqs:GetQueueAttributes`, `sqs:SetQueueAttributes`, `sns:Subscribe` | SNS actions on the generated `bus-*` topics; SQS actions on the generated `bus-*` consumer-identity queues |
| Queue consumer | `sqs:ReceiveMessage`, `sqs:DeleteMessage`, `sqs:ChangeMessageVisibility` | `sqs:CreateQueue` on startup | The consumer's `queue-*` destinations |
| SNS service principal (the queue policy the Bus consumer deployment writes) | `sqs:SendMessage` | None | The provider writes the queue policy for principal `sns.amazonaws.com`, resource = that consumer-identity queue ARN, and `aws:SourceArn` = the subscribing `bus-*` topic ARN |

With `AutoProvision = false`, a consumer needs `sqs:GetQueueUrl`, `sqs:ReceiveMessage`, `sqs:DeleteMessage`, and `sqs:ChangeMessageVisibility`. A Bus publisher needs `sns:ListTopics` and `sns:Publish`. A Queue publisher needs `sqs:GetQueueUrl` and `sqs:SendMessage`.

- A consumer topology denial surfaces as `AWS_MESSAGING_PROVISIONING_DENIED` with the lane, subscription name, AWS error code, and the action set for that stage. Use the denied AWS API operation to find the exact missing action.
- A publisher denial returns a failed publish with the AWS exception message and retries as described in [Publish retries](retries.md#publish-retries). A receive denial is logged and retried with back-off.
- Do not grant delete, wildcard SNS/SQS administration, or unrelated IAM actions. The transport does not call them.

### Design and runtime behavior

- **Redrive is yours.** Configure a dead-letter queue and a redrive policy with a bounded `maxReceiveCount` for handler failures. Headless creates no redrive infrastructure. It deletes malformed envelopes and messages poisoned on arrival instead of dead-lettering them, because SQS moves a message only after `maxReceiveCount` receives, which would re-run the poison handling that many times with no reason attached.
- **Rejects.** A handler rejection makes the message visible again after 3 seconds, which is a normal SQS retry and counts toward a redrive policy. Rejects to one queue are batched (`ChangeMessageVisibilityBatch`, up to ten). A message no longer in flight is logged, not thrown.
- **Visibility heartbeat.** A receive takes up to ten messages, and each stays hidden until the core commits or rejects it, including messages waiting for a free handler slot under the consumer's `Concurrency`. Every third of `VisibilityTimeout`, the client extends all unsettled messages by `VisibilityTimeout`. A message SQS refuses to extend because its receipt handle is no longer current is dropped from the heartbeat with a warning, because it may be redelivered. SQS caps total invisibility at 12 hours from the first receive. A reject never gets overridden by a later extension.
- **Receive loops.** Each polled queue URL has its own loop, so a long poll on an empty queue never delays a busy one. A Queue-lane client polls one URL per message name. An empty poll waits one `ConsumerPollTimeout` before the next. Without a `Concurrency` bound the client handles one message at a time across all its loops.
- **Deletes.** Commits to one queue share `DeleteMessageBatch` calls when they overlap. A lone delete adds no wait. A stale receipt handle is logged, not thrown.
- **Broker reachability.** A failed receive is reported as unreachable and the loop retries in place; see [Broker reachability](operations.md#broker-reachability).
- **Shutdown.** The client stops receiving, then waits for the receive loop and running handlers within the shutdown budget (30 seconds when disposed directly). The heartbeat keeps running meanwhile. Messages still unsettled afterwards are made visible at once instead of after their timeout.
- **Raw producers.** For non-Headless producers, a Bus contract on a raw lane needs its consumer identity's queue to itself, and the header attribute is optional; see [Foreign producers and raw payloads](foreign-producers.md#foreign-producers-and-raw-payloads).

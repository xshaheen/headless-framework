---
domain: Messaging
packages: Messaging.Aws
---

# AWS SNS/SQS transport

> Part of [Messaging](../messaging.md). The `Headless.Messaging.Aws` transport: SNS topics for the Bus lane, SQS queues for the Queue lane, deploy-time topology through `resources provision`, FIFO support, and the IAM actions each workload needs.

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

Then create the topology as a deploy step, before the new release serves traffic. Its kind is `AmazonSqs` (`SetupMessagingAws.TopologyResourceKind`):

```bash
dotnet MyApp.dll resources provision -k AmazonSqs
```

**Verify:** `resources provision` prints the `AmazonSqs` `topology` line with status `Ok`, then the `warmup` readiness check turns `Healthy` once the host's [topology check](transports.md#topology-check) passes, and `messaging-broker` reports `Healthy` after the first receive or send ([Broker reachability](operations.md#broker-reachability)). Until provisioning runs, `warmup` stays `Unhealthy` and names every missing object, such as `Amazon SQS queue 'queue-orders-place.fifo' does not exist. Run 'resources provision' ...`. The `describe` host manifest's `messaging.transports` shows `"bus": "aws.sns"` and `"queue": "aws_sqs"`.

### Names and topology

- Bus publishes to the SNS topic `bus-{logical-name}`. Each Bus consumer identity owns the SQS queue `bus-{consumer-identity}`, subscribed to that topic. Queue sends go to `queue-{logical-name}`. In names, `.` becomes `-` and `:` becomes `_`.
- With `Headless:Prefix` set to `wt1`, every topic and queue starts with `wt1_` (`wt1_bus-orders-created`), and the IAM resource patterns below become `wt1_bus-*` and `wt1_queue-*`. See [host prefix](transports.md#host-prefix).
- Bus and Queue have independent topology, so one contract name can carry both lanes without cross-delivery.
- Consumer startup honors host cancellation through the SNS/SQS lookups.

**What the topology resource does:**

- `provision` creates one SNS topic per registered Bus message, one SQS queue per competing Bus consumer identity and per registered Queue-lane message (consumed on this host or not), each identity queue's access policy for its topics, and a raw-delivery subscription from each topic to each identity queue that consumes it. Each create is idempotent, and the queue policy is written only when a topic is missing from it, so a second run changes nothing.
- `verify` writes nothing. It costs one `sns:ListTopics` per 100 topics in the account and region (SNS has no lookup by name), one `sqs:GetQueueUrl` per queue, and for each identity queue one `sqs:GetQueueAttributes` (ARN and policy), one `sns:ListSubscriptionsByTopic` per 100 subscriptions of each of its topics, and one `sns:GetSubscriptionAttributes` per subscription of that queue to check raw delivery. It names every missing topic, queue, policy grant, and raw subscription in one failure.
- `purge` runs `PurgeQueue` on each queue this host consumes. SQS allows one purge per queue every 60 seconds; a purge already running counts as done.
- `delete` removes the SNS subscriptions feeding each queue this host consumes, then the queue. A queue already gone is skipped.
- `status` reports each queue's visible and in-flight message counts, and each missing object.
- Purge and delete never touch a queue the host only sends to, because it belongs to the service that consumes it. Topics are never deleted: every service that publishes or consumes a message shares its topic, and a publish-only service leaves no subscription that shows it still uses one. Delete a topic by hand once no service publishes it.
- An infrastructure-as-code deployment may create the same objects instead. `resources verify` then checks them, and each Bus subscription must enable raw message delivery.

**What the running host does:**

- Consumer clients create nothing. A missing queue fails the consumer with `ResourceProvisioningException` naming `resources provision`, and the readiness gate holds meanwhile.
- A Bus publisher lists the topics once per process and looks up a topic that was not listed. A missing topic fails the send naming `resources provision`. After a miss, sends to that topic fail without a new `sns:ListTopics` lookup for 30 seconds, so a topic provisioned while the host runs is found within that window.
- A Queue publisher looks a queue up with `sqs:GetQueueUrl` before its first send in each process. A missing queue fails the send naming `resources provision`, and the next send looks again.
- An `IRuntimeSubscriber` subscription still creates its topic, identity queue, queue policy, and raw-delivery subscription when it attaches, because `resources provision` never sees it.
- Not supported: every-instance consumers (startup fails naming the consumer) and request/reply (a host that sends requests or declares a responder fails startup).

### Configuration

Configure region, service URLs, and credentials through `AmazonSqsMessagingOptions` (or the AWS SDK default credential chain). Its other settings:

| Option | Default | Effect |
| --- | --- | --- |
| `VisibilityTimeout` | 30 s | Sent on every receive, so the queue's own default does not apply. Whole seconds from 1 second to 12 hours |
| `ReceiveWaitTime` | 5 s | SQS long-poll wait per receive. Whole seconds from 0 to 20 |

**FIFO and routing affinity.** `RoutingAffinityKey` maps to the native `MessageGroupId` only for registered `.fifo` SNS topics or SQS queues. Keys are 1 to 128 printable ASCII characters (`!` to `~`), without spaces. `MessageGroupId(...)` on the lane builder and the `AwsMessagingHeaders.MessageGroupId` header are raw adapters. They must agree with a typed key when you set both. `MessageGroupId(...)` is message-side only, because it is stamped at publish, and a value longer than 128 characters is rejected. On a standard (non-FIFO) route, a typed key travels in the header bag only, without those length and character limits, and gives no affinity guarantee.

**FIFO consumption order.** On a FIFO queue the consumer reads each message's `MessageGroupId`, takes a group's messages in one at a time in receive order, and returns the rest of a group's batch to the queue as soon as one of them is rejected, hidden as long as the rejected message. Admission to the inbox therefore keeps the group's order. Handlers run after admission, so a shared group does not make handling exclusive on its own. Tune the consumer `Sequential()` with the group as its `RoutingAffinityKey`, or run it at `Concurrency(1)`. See [Transport-Specific Ordering](ordering.md#transport-specific-ordering).

**Envelope format.** Every send puts the whole header dictionary into one String attribute named `headless-aws-headers-v1`: an SQS message attribute on the Queue lane and an SNS message attribute on the Bus lane. The body and the native `MessageGroupId` are unchanged. Bus subscriptions use SNS raw message delivery (`RawMessageDelivery = true`), so SQS receives the body and attribute as sent. `resources provision` and a runtime subscription set raw delivery on a subscription they create and on one that already exists (`sns:SetSubscriptionAttributes`). One attribute stays inside the SQS limit of ten message attributes. A message whose attribute is missing (including an SNS-wrapped body from a subscription without raw delivery), mixed with other attributes, malformed, or holding duplicate or reserved header names is deleted from the queue without reaching a handler.

### AWS permissions

`AmazonSqsMessagingOptions.Credentials`, or the SDK default chain, supplies the identity. Run `resources provision` under a deploy identity with the create rights, and give the running host only the read and data actions. Grant only the rows each workload uses:

| Workload | Running host | `resources provision` (deploy identity) | Resource scope |
| --- | --- | --- | --- |
| Bus publisher | `sns:ListTopics`, `sns:Publish` | `sns:CreateTopic` | `sns:ListTopics` requires `Resource: "*"`; scope create and publish to `arn:${Partition}:sns:${Region}:${Account}:bus-*` |
| Queue publisher | `sqs:GetQueueUrl`, `sqs:SendMessage` | `sqs:CreateQueue` | `arn:${Partition}:sqs:${Region}:${Account}:queue-*` |
| Bus consumer | `sqs:GetQueueUrl`, `sqs:ReceiveMessage`, `sqs:DeleteMessage`, `sqs:ChangeMessageVisibility`; for the topology check, `sns:ListTopics`, `sqs:GetQueueAttributes`, `sns:ListSubscriptionsByTopic`, `sns:GetSubscriptionAttributes` | `sns:CreateTopic`, `sqs:CreateQueue`, `sqs:GetQueueAttributes`, `sqs:SetQueueAttributes`, `sns:Subscribe`, `sns:SetSubscriptionAttributes` | SNS actions on the generated `bus-*` topics; SQS actions on the generated `bus-*` consumer-identity queues |
| Queue consumer | `sqs:GetQueueUrl`, `sqs:ReceiveMessage`, `sqs:DeleteMessage`, `sqs:ChangeMessageVisibility` | `sqs:CreateQueue` | The consumer's `queue-*` destinations |
| `IRuntimeSubscriber` subscription | The Bus consumer's running-host actions, plus its `resources provision` actions, because the subscription creates its own topology | None | The runtime subscription's `bus-*` topics and identity queue |
| `resources purge` and `resources delete` | None | `sqs:GetQueueUrl`, `sqs:PurgeQueue`; for delete also `sns:ListTopics`, `sqs:GetQueueAttributes`, `sns:ListSubscriptionsByTopic`, `sns:Unsubscribe`, `sqs:DeleteQueue` | The queues this host consumes. Grant these only to the environment that resets or tears down |
| SNS service principal (the queue policy `resources provision` writes) | `sqs:SendMessage` | None | The policy names principal `sns.amazonaws.com`, resource = that consumer-identity queue ARN, and `aws:SourceArn` = the subscribing `bus-*` topic ARN |

To drop the readiness check, and with it the running host's `sns:ListSubscriptionsByTopic`, `sns:GetSubscriptionAttributes`, and `sqs:GetQueueAttributes` rights, call `services.ConfigureResourceProvisioning(o => o.StartupChecks[SetupMessagingAws.TopologyResourceKind] = ResourceStartupCheck.None)`. A missing queue then surfaces only when a consumer starts on it.

- A consumer topology denial surfaces as `AWS_MESSAGING_PROVISIONING_DENIED` with the lane, subscription name, AWS error code, and the action set for that stage. Use the denied AWS API operation to find the exact missing action.
- A publisher denial returns a failed publish with the AWS exception message and retries as described in [Publish retries](retries.md#publish-retries). A receive denial is logged and retried with back-off.
- Do not grant delete, purge, wildcard SNS/SQS administration, or unrelated IAM actions to the running host. It does not call them.

### Design and runtime behavior

- **Redrive is yours.** Configure a dead-letter queue and a redrive policy with a bounded `maxReceiveCount` for handler failures. Headless creates no redrive infrastructure. It deletes malformed envelopes and messages poisoned on arrival instead of dead-lettering them, because SQS moves a message only after `maxReceiveCount` receives, which would re-run the poison handling that many times with no reason attached.
- **Rejects.** A handler rejection makes the message visible again after 3 seconds, which is a normal SQS retry and counts toward a redrive policy. Rejects to one queue are batched (`ChangeMessageVisibilityBatch`, up to ten). A message no longer in flight is logged, not thrown.
- **Visibility heartbeat.** A receive takes up to ten messages, and each stays hidden until the core commits or rejects it, including messages waiting for a free handler slot under the consumer's `Concurrency`. Every third of `VisibilityTimeout`, the client extends all unsettled messages by `VisibilityTimeout`. A message SQS refuses to extend because its receipt handle is no longer current is dropped from the heartbeat with a warning, because it may be redelivered. SQS caps total invisibility at 12 hours from the first receive. A reject never gets overridden by a later extension.
- **Receive loops.** Each polled queue URL has its own loop, so a long poll on an empty queue never delays a busy one. A Queue-lane client polls one URL per message name. An empty poll waits one `ConsumerPollTimeout` before the next. Without a `Concurrency` bound the client handles one message at a time across all its loops.
- **Deletes.** Commits to one queue share `DeleteMessageBatch` calls when they overlap. A lone delete adds no wait. A stale receipt handle is logged, not thrown.
- **Broker reachability.** A failed receive is reported as unreachable and the loop retries in place; see [Broker reachability](operations.md#broker-reachability).
- **Shutdown.** The client stops receiving, then waits for the receive loop and running handlers within the shutdown budget (30 seconds when disposed directly). The heartbeat keeps running meanwhile. Messages still unsettled afterwards are made visible at once instead of after their timeout. On a FIFO queue, the rest of a group behind a message still in flight stays hidden for `VisibilityTimeout`, and the rest behind a rejected message for its reject delay, so a group never comes back out of order. A message the client could not give back reappears once its visibility runs out.
- **Raw producers.** For non-Headless producers, a Bus contract on a raw lane needs its consumer identity's queue to itself, and the header attribute is optional; see [Foreign producers and raw payloads](foreign-producers.md#foreign-producers-and-raw-payloads).

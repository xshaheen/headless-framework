## Residual Review Findings

Branch `feat/messaging-queue-request-reply` (issue #222). The review ran at d74ce2516 and found no P0 or P1 issues; its verdict was "ready with fixes". These fixes landed in 93862a1ec:

- jittered reply-listener backoff;
- a shutdown abort is now treated as a shutdown;
- the failure contract of `RequestNotSentException`;
- a bounded reply send;
- expiry of a crash-recovered request;
- Queue-lane gating;
- the race between registering and arming a pending call;
- sanitized fault text.

The findings below were not applied in this branch.

- P3: The NATS, Redis, and RabbitMQ reply listeners each carry their own reconnect loop around the shared Core helpers. Filed as https://github.com/xshaheen/headless-framework/issues/1068.
- P2: Hardening items:
  - nested requests do not inherit the remaining deadline;
  - a requeued stored request cannot rerun;
  - `RequestOptions.Timeout` has no upper bound;
  - startup does not check NATS stream subjects against the reply subjects;
  - startup rejection is not proven in each provider;
  - the tenant probe is duplicated;
  - the `skipped` receive outcome now also counts no-responder requests;
  - several test gaps remain.

  Filed as https://github.com/xshaheen/headless-framework/issues/1069.
- P2: Azure Service Bus rejects request/reply at startup. Filed as https://github.com/xshaheen/headless-framework/issues/1070.
- P3: Pulsar rejects request/reply at startup. Filed as https://github.com/xshaheen/headless-framework/issues/1071.
- P2: `PropagateTenant()` covers the Bus lane only. Plain Queue enqueues and Queue consumers get no ambient tenant. Filed as https://github.com/xshaheen/headless-framework/issues/1072.
- P3: The first version leaves out these capabilities:
  - broker TTL derived from the request deadline (general message TTL is tracked in #1053);
  - reply headers;
  - alternate response types;
  - a cap on pending calls.

  Filed as https://github.com/xshaheen/headless-framework/issues/1073.

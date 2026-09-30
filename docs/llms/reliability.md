---
domain: Reliability
packages: Reliability.Abstractions
---

# Reliability

> Reliability contracts that Messaging and Jobs share. Today this is the failure policy a handler names in its declaration.

## Orientation

- **`Headless.Reliability.Abstractions`** holds `IFailurePolicy`. Messaging and Jobs abstractions reference it, so a policy type written once works for consumers and jobs alike.
- The runtime retry behavior is still configured per subsystem: `MessagingOptions.RetryPolicy` for Messaging ([messaging.md](messaging.md)) and `ConfigureRetries` for Jobs ([jobs.md](jobs.md)).

## Agent Rules

- Name a failure policy by type, never by string, so a misspelled policy fails the build.
- `IFailurePolicy` carries no members yet. Until a policy model is supplied for the declared type, a handler that names a policy runs with its host's configured retry behavior. Do not build parallel retry infrastructure around the marker.

## Headless.Reliability.Abstractions

Namespace: `Headless.Reliability`.

| Type | Purpose |
| --- | --- |
| `IFailurePolicy` | Marks a type that a message consumer or job names as its failure policy. |

A handler's effective policy resolves in order: the call site, then the handler's declaration, then the host.

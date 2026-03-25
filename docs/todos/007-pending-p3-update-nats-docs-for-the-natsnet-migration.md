---
status: pending
priority: p3
issue_id: "007"
tags: ["code-review","documentation","agent-native"]
dependencies: []
---

# Update NATS docs for the NATS.Net migration

## Problem Statement

The public NATS README and generated agent-context docs still point to the old package/dependency names and removed options (`UseNATS`, `Headless.Messaging.NATS`, `EnableJetStream`, `NATS.Client`). Consumers and agents following these docs will generate non-compiling setup code after the migration.

## Findings

- **Location:** src/Headless.Messaging.Nats/README.md:20-53; docs/llms/messaging.md:1197-1220
- **Propagation:** The broken guidance is also surfaced through the repo's agent-context files
- **Discovered by:** code review

## Proposed Solutions

### Patch the NATS package README
- **Pros**: Fixes the source document
- **Cons**: Still needs LLM doc regeneration
- **Effort**: Small
- **Risk**: Low

### Patch README + regenerate llms docs together
- **Pros**: Fixes both human and agent guidance in one pass
- **Cons**: Touches more files
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Update the NATS README to the current API and regenerate the LLM docs from that corrected source.

## Acceptance Criteria

- [ ] README examples use `UseNats` and the current option names
- [ ] Dependency bullets reference `NATS.Net` correctly
- [ ] Generated LLM docs no longer instruct users to use removed APIs

## Notes

Review of branch xshaheen/review-transports on 2026-03-25.

## Work Log

### 2026-03-25 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

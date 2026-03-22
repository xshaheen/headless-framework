---
status: pending
priority: p1
issue_id: "011"
tags: ["code-review","correctness"]
dependencies: []
---

# Fix RecordingTransport type resolution — WaitForPublished<T> broken

## Problem Statement

RecordingTransport.SendAsync calls Type.GetType(messageTypeName) to resolve the message type from headers. However, the framework (IMessagePublishRequestFactory.cs:92) writes only messageType.Name (e.g. 'OrderCreated') — a short unqualified name. Type.GetType() requires an assembly-qualified name and returns null for anything outside mscorlib. This means ALL published messages are recorded as typeof(TransportMessage), making WaitForPublished<T>() always time out for any specific T. The unit test passes only because it uses .AssemblyQualifiedName directly, which never occurs in production.

## Findings

- **RecordingTransport:** src/Headless.Messaging.Testing/Internal/RecordingTransport.cs:28
- **Header source:** src/Headless.Messaging.Core/Internal/IMessagePublishRequestFactory.cs:92 — writes messageType.Name
- **Test masking bug:** tests/.../RecordingInfrastructureTests.cs:179 — uses .AssemblyQualifiedName! (not realistic)
- **Impact:** WaitForPublished<T>() is completely broken for typed matching
- **Discovered by:** security-sentinel, strict-dotnet-reviewer

## Proposed Solutions

### Option 1: Assembly scan fallback in RecordingTransport
- **Pros**: No wire format change, contained to test package
- **Cons**: O(N) assembly scan, but test-only code
- **Effort**: Small
- **Risk**: Low

### Option 2: Change IMessagePublishRequestFactory to write AssemblyQualifiedName
- **Pros**: Root cause fix
- **Cons**: Wire format change affects all providers
- **Effort**: Medium
- **Risk**: Medium


## Recommended Action

Option 1 — add assembly scan fallback in RecordingTransport. Test-only impact, no wire format change.

## Acceptance Criteria

- [ ] WaitForPublished<OrderCreatedEvent>() matches correctly in E2E test
- [ ] RecordingTransport records the concrete message type, not TransportMessage
- [ ] Unit test uses realistic header values (not .AssemblyQualifiedName)

## Notes

No existing E2E test exercises WaitForPublished<T>() — all use WaitForConsumed. Add a test.

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

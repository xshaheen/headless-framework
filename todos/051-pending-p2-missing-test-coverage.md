---
status: pending
priority: p2
issue_id: "051"
tags: [code-review, testing, aws-sqs, quality]
created: 2026-01-20
dependencies: [046, 047, 048, 049, 050]
---

# Missing Test Coverage for AWS SQS Provider

## Problem

**No test files** found for Framework.Messages.AwsSqs. Zero unit/integration tests for critical messaging infrastructure.

**Impact:** Regression risk, integration bugs, unvalidated thread safety, no verification of fixes.

## Critical Untested Scenarios

1. Topic normalization logic (has bug #050)
2. Concurrent connection initialization (thread safety)
3. AWS policy generation/compaction
4. Message serialization/deserialization failures
5. GetAwaiter().GetResult() deadlock (#046)
6. Semaphore release in exception paths (#049)
7. Dictionary race conditions (#048)

## Solution

Create test project: `tests/Framework.Messages.AwsSqs.Tests.Integration/`

Use **LocalStack** for AWS mocking or AWS SDK mocks.

**Test Structure:**
```
tests/Framework.Messages.AwsSqs.Tests.Integration/
├── AmazonSqsTransportTests.cs (transport publish)
├── AmazonSqsConsumerClientTests.cs (consume, ack, reject)
├── TopicNormalizerTests.cs (validation logic)
├── AmazonPolicyExtensionsTests.cs (IAM policies)
└── ConcurrencyTests.cs (thread safety, race conditions)
```

**Coverage Targets:**
- Line: ≥85%
- Branch: ≥80%
- Mutation: ≥70%

## Acceptance Criteria

- [ ] Create test project with Testcontainers/LocalStack
- [ ] Add unit tests for TopicNormalizer (100% coverage)
- [ ] Add unit tests for AmazonPolicyExtensions
- [ ] Add integration tests: publish → subscribe → consume flow
- [ ] Add concurrency tests: 100 parallel SendAsync
- [ ] Run coverage analysis: `./build.sh CoverageAnalysis --test-project Framework.Messages.AwsSqs.Tests.Integration`
- [ ] Meet coverage thresholds

**Effort:** 2 sprints | **Risk:** Medium

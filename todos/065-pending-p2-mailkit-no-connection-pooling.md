# No Connection Pooling - New Client Per Email

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, performance, emails, mailkit

---

## Problem Statement

Every `SendAsync` creates a new `SmtpClient` with full TCP handshake, TLS negotiation, and authentication. This is **O(n) connection overhead**.

**Location:** `src/Framework.Emails.Mailkit/MailkitEmailSender.cs:23-26`

```csharp
using var client = await _BuildClientAsync(settings, cancellationToken);
await client.SendAsync(mimeMessage, cancellationToken);
await client.DisconnectAsync(quit: true, cancellationToken);
```

**Per-email overhead:**
- TCP handshake: ~10-50ms
- TLS negotiation: ~20-100ms
- SMTP AUTH: ~10-50ms
- **Total: 40-200ms per email**

**Impact at scale:**
| Emails/sec | Connection Overhead | Throughput Impact |
|------------|---------------------|-------------------|
| 10 | 400-2000ms | Acceptable |
| 100 | 4-20 seconds | **Unacceptable** |
| 1000 | 40-200 seconds | **Critical** |

**Port exhaustion risk:** 1000+ emails/minute can exhaust ephemeral ports due to TIME_WAIT.

---

## Proposed Solutions

### Option A: Connection Pool with Channels (Recommended)

```csharp
public sealed class MailkitEmailSender : IEmailSender, IAsyncDisposable
{
    private readonly Channel<SmtpClient> _pool;

    private async ValueTask<SmtpClient> _RentClientAsync(...);
    private async ValueTask _ReturnClientAsync(SmtpClient client, ...);

    public async ValueTask DisposeAsync() { /* drain pool */ }
}
```

- **Effort:** Medium
- **Risk:** Low
- **Gain:** 8-40x throughput improvement

### Option B: Background Service with Queue

```csharp
public class MailkitEmailSender : BackgroundService, IEmailSender
{
    private readonly Channel<EmailWorkItem> _queue;
    private SmtpClient? _client;  // Single persistent connection

    protected override async Task ExecuteAsync(CancellationToken ct) { /* process queue */ }
}
```

- **Effort:** Medium
- **Risk:** Low - simpler model
- **Gain:** Connection reuse, natural batching

### Option C: Document as Limitation

Add documentation that this implementation is for low-volume use.

- **Effort:** Trivial
- **Risk:** Users hit performance wall unexpectedly

---

## Recommended Action

Option A for flexibility. Option B if single-server is sufficient.

---

## Expected Performance Gains

| Metric | Before | After (Pool=10) |
|--------|--------|-----------------|
| Connection setup | 40-200ms/email | 0ms (reused) |
| Throughput | ~5-25 emails/sec | ~200 emails/sec |
| Port usage | Unbounded | Bounded |

---

## Acceptance Criteria

- [ ] Implement connection pooling or document limitation
- [ ] Add `MaxPoolSize` option
- [ ] Handle stale connections (NoOp check)
- [ ] Add pool metrics/logging

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From performance-oracle + pragmatic-dotnet-reviewer |

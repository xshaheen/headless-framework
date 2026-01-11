# Inline _ConfigureClient Method

**Date:** 2026-01-11
**Status:** pending
**Priority:** P3 - Nice-to-have
**Tags:** code-review, emails, mailkit, cleanup

---

## Problem Statement

`_ConfigureClient` is a separate method called only from `_BuildClientAsync`. With only 4-6 lines, inlining reduces indirection.

**Location:** `src/Framework.Emails.Mailkit/MailkitEmailSender.cs:54-71`

```csharp
private static async Task _ConfigureClient(
    SmtpClient client,
    MailkitSmtpOptions options,
    CancellationToken cancellationToken
)
{
    await client.ConnectAsync(...);
    if (options.RequiresAuthentication)
    {
        await client.AuthenticateAsync(options.User, options.Password, cancellationToken);
    }
}
```

---

## Proposed Solutions

### Option A: Inline into _BuildClientAsync

```csharp
private static async Task<SmtpClient> _BuildClientAsync(...)
{
    var client = new SmtpClient();
    try
    {
        await client.ConnectAsync(
            options.Server,
            options.Port,
            options.SocketOptions ?? SecureSocketOptions.StartTls,
            cancellationToken).AnyContext();

        if (options.HasCredentials)
        {
            await client.AuthenticateAsync(options.User, options.Password, cancellationToken).AnyContext();
        }

        return client;
    }
    catch
    {
        client.Dispose();
        throw;
    }
}
```

- **Effort:** Trivial
- **Risk:** None
- **Gain:** ~5 LOC, one less method

---

## Acceptance Criteria

- [ ] Inline `_ConfigureClient` into `_BuildClientAsync`
- [ ] Remove `#region` markers while at it

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code-simplicity-reviewer |

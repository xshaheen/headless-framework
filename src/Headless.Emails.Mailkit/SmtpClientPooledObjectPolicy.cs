// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;

namespace Headless.Emails.Mailkit;

internal sealed class SmtpClientPooledObjectPolicy(IOptionsMonitor<MailkitSmtpOptions> options)
    : IPooledObjectPolicy<SmtpClient>
{
    public SmtpClient Create()
    {
        return new SmtpClient { Timeout = (int)options.CurrentValue.Timeout.TotalMilliseconds };
    }

    public bool Return(SmtpClient client)
    {
        // Discard disconnected, faulted, or connected-but-unauthenticated clients so a pooled
        // connection is never reused in a state that would skip authentication on the next send
        // (e.g. after an AuthenticationException leaves the client connected but not authenticated).
        if (!client.IsConnected || (options.CurrentValue.HasCredentials && !client.IsAuthenticated))
        {
            client.Dispose();
            return false;
        }

        return true;
    }
}

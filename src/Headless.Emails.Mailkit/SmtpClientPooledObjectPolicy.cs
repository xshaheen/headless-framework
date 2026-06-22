// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;

namespace Headless.Emails.Mailkit;

internal sealed class SmtpClientPooledObjectPolicy(IOptionsMonitor<MailkitSmtpOptions> options, string? optionsName)
    : IPooledObjectPolicy<SmtpClient>
{
    public SmtpClient Create()
    {
        // Read the snapshot for this instance's options name (null = the default pool). A keyed pool must not
        // read CurrentValue, which always binds the default options.
        return new SmtpClient { Timeout = (int)options.Get(optionsName).Timeout.TotalMilliseconds };
    }

    public bool Return(SmtpClient client)
    {
        // Discard disconnected or faulted clients
        if (!client.IsConnected)
        {
            client.Dispose();
            return false;
        }

        return true;
    }
}

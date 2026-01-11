// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;

namespace Framework.Emails.Mailkit;

internal sealed class SmtpClientPooledObjectPolicy(IOptionsMonitor<MailkitSmtpOptions> options)
    : IPooledObjectPolicy<SmtpClient>
{
    public SmtpClient Create()
    {
        var client = new SmtpClient();
        client.Timeout = (int)options.CurrentValue.Timeout.TotalMilliseconds;
        return client;
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

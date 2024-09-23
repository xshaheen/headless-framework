// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Emails.Dev;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Framework.Emails;

public static class AddDevEmailExtensions
{
    public static void AddDevEmailSender(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IEmailSender, DevEmailSender>();
    }

    public static void AddNoopEmailSender(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IEmailSender, NoopEmailSender>();
    }
}

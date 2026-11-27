// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting;

namespace Headless.DevInbox.Dashboard.Internal;

/// <summary>
/// Fails the host's start when the Dev inbox module is registered outside Development or with no capture store, so a
/// production host never serves captured messages and a misconfigured one says so on its first start.
/// </summary>
internal sealed class DevInboxStartupValidator(IServiceProvider services) : IHeadlessStartupValidator
{
    private readonly IServiceProvider _services = Argument.IsNotNull(services);

    public Task ValidateAsync(CancellationToken cancellationToken)
    {
        DevInboxStores.Resolve(_services);

        return Task.CompletedTask;
    }
}

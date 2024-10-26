// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Permissions.Filters;

namespace Framework.Permissions.Testing;

public sealed class AlwaysAllowMethodInvocationAuthorizationService : IMethodInvocationAuthorizationService
{
    public Task CheckAsync(MethodInvocationAuthorizationContext context)
    {
        return Task.CompletedTask;
    }
}

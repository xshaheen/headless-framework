// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Reflection;

namespace Framework.Permissions;

public interface IMethodInvocationAuthorizationService
{
    Task CheckAsync(MethodInvocationAuthorizationContext context);
}

public sealed record MethodInvocationAuthorizationContext(MethodInfo Method);

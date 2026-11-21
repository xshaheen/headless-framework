// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using DotNet.Testcontainers.Containers;
using Headless.Checks;

namespace Tests;

/// <summary>
/// Forwards to a real container, but reports another host port for one container port until the first stop, as a
/// desktop Docker engine does when another host listener holds the port it handed out.
/// </summary>
public class ShadowedContainer : DispatchProxy
{
    private IContainer _inner = null!;
    private int _containerPort;
    private ushort _shadowedPort;

    /// <summary>Gets how many times the container was stopped through this proxy.</summary>
    public int Stops { get; private set; }

    /// <summary>Wraps <paramref name="inner"/>, reporting <paramref name="shadowedPort"/> for <paramref name="containerPort"/>.</summary>
    public static (IContainer Container, ShadowedContainer Proxy) Create(
        IContainer inner,
        int containerPort,
        ushort shadowedPort
    )
    {
        var container = Create<IContainer, ShadowedContainer>();
        var proxy = (ShadowedContainer)(object)container;
        proxy._inner = inner;
        proxy._containerPort = containerPort;
        proxy._shadowedPort = shadowedPort;

        return (container, proxy);
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        Argument.IsNotNull(targetMethod);

        if (
            Stops == 0
            && string.Equals(targetMethod.Name, nameof(IContainer.GetMappedPublicPort), StringComparison.Ordinal)
            && args is [int port]
            && port == _containerPort
        )
        {
            return _shadowedPort;
        }

        if (string.Equals(targetMethod.Name, nameof(IContainer.StopAsync), StringComparison.Ordinal))
        {
            Stops++;
        }

        try
        {
            return targetMethod.Invoke(_inner, args);
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            throw e.InnerException;
        }
    }
}

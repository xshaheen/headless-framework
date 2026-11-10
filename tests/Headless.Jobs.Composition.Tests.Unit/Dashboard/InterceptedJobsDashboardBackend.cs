// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Runtime.ExceptionServices;
using Headless.Jobs;

namespace Tests.Dashboard;

/// <summary>
/// The real dashboard backend with one seam: every call passes through a test's interceptor, which can answer it, fail
/// it, or let it reach the store. A test uses it to lose a race the store decides (a row changed between the rule
/// check and the write) or to fail a read after a write committed, without implementing every member by hand.
/// </summary>
#pragma warning disable CA1852 // Cannot seal: DispatchProxy subclasses this type when the proxy is created.
internal class InterceptedJobsDashboardBackend : DispatchProxy
#pragma warning restore CA1852
{
    private IJobsDashboardBackend _inner = null!;
    private Func<string, object?[], Func<object?>, object?> _intercept = null!;

    /// <summary>
    /// Wraps <paramref name="inner"/>. <paramref name="intercept"/> receives each call's method name, its arguments,
    /// and a function that makes the call on the real backend.
    /// </summary>
    public static IJobsDashboardBackend Wrap(
        IJobsDashboardBackend inner,
        Func<string, object?[], Func<object?>, object?> intercept
    )
    {
        var proxy = Create<IJobsDashboardBackend, InterceptedJobsDashboardBackend>();
        var typed = (InterceptedJobsDashboardBackend)(object)proxy;
        typed._inner = inner;
        typed._intercept = intercept;

        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        var method = targetMethod ?? throw new InvalidOperationException("The proxy was called with no method.");
        var arguments = args ?? [];

        return _intercept(
            method.Name,
            arguments,
            () =>
            {
                try
                {
                    return method.Invoke(_inner, arguments);
                }
                catch (TargetInvocationException exception) when (exception.InnerException is { } inner)
                {
                    ExceptionDispatchInfo.Throw(inner);

                    throw;
                }
            }
        );
    }
}

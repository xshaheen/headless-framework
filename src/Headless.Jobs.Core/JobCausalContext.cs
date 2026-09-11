// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Base;

namespace Headless.Jobs;

internal static class JobCausalContext
{
    private static readonly AsyncLocal<JobFunctionContext?> _Current = new();

    internal static JobFunctionContext? Current => _Current.Value;

    internal static IDisposable Enter(JobFunctionContext context)
    {
        var previous = _Current.Value;
        _Current.Value = context;
        return new Scope(previous);
    }

    private sealed class Scope(JobFunctionContext? previous) : IDisposable
    {
        public void Dispose() => _Current.Value = previous;
    }
}

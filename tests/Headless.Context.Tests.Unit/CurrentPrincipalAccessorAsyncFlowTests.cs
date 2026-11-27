// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using System.Security.Claims;
using Headless.Context;

namespace Tests;

/// <summary>
/// The principal override rides on <see cref="AsyncLocal{T}"/>, so it must follow the async flow whether the compiler
/// generates state machines or the runtime runs async methods itself (runtime async on .NET 11).
/// </summary>
public sealed class CurrentPrincipalAccessorAsyncFlowTests
{
    private readonly ThreadCurrentPrincipalAccessor _accessor = new();

    [Fact]
    public async Task should_keep_the_principal_after_an_await_that_resumes_on_another_thread()
    {
        var principal = _Principal("outer");
        using var _ = _accessor.Change(principal);
        var before = Environment.CurrentManagedThreadId;

        await new ResumeOnNewThread();

        Environment.CurrentManagedThreadId.Should().NotBe(before);
        _accessor.Principal.Should().BeSameAs(principal);
    }

    [Fact]
    public async Task should_not_leak_a_principal_set_inside_an_awaited_method_back_to_the_caller()
    {
        var outer = _Principal("outer");
        var inner = _Principal("inner");
        using var _ = _accessor.Change(outer);

        var seenInside = await _ChangeAndResumeElsewhereAsync(inner);

        seenInside.Should().BeSameAs(inner);
        _accessor.Principal.Should().BeSameAs(outer);
    }

    private async Task<ClaimsPrincipal?> _ChangeAndResumeElsewhereAsync(ClaimsPrincipal principal)
    {
#pragma warning disable CA2000, IDISP004 // Left undisposed on purpose: the test checks that the async flow alone keeps the change from reaching the caller.
        _accessor.Change(principal);
#pragma warning restore CA2000, IDISP004

        await new ResumeOnNewThread();

        return _accessor.Principal;
    }

    private static ClaimsPrincipal _Principal(string name) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, name)], authenticationType: "test"));

    // Resumes the awaiting method on a thread of its own, so the test sees a real thread hop instead of trusting the
    // thread pool to pick another worker.
    private readonly struct ResumeOnNewThread : INotifyCompletion
    {
        public ResumeOnNewThread GetAwaiter() => this;

        public bool IsCompleted => false;

        public void OnCompleted(Action continuation) =>
            new Thread(() => continuation()) { IsBackground = true }.Start();

        public void GetResult() { }
    }
}

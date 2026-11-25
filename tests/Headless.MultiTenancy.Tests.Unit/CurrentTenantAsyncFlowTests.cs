// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.MultiTenancy;
using Headless.Primitives;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// The tenant scope rides on <see cref="AsyncLocal{T}"/>, so it must follow the async flow whether the compiler
/// generates state machines or the runtime runs async methods itself (runtime async on .NET 11).
/// </summary>
public sealed class CurrentTenantAsyncFlowTests : TestBase
{
    private readonly CurrentTenant _currentTenant = new(AsyncLocalCurrentTenantAccessor.Instance);

    [Fact]
    public async Task should_keep_the_tenant_after_an_await_that_resumes_on_another_thread()
    {
        using var _ = _currentTenant.Change("tenant-a", "Tenant A");
        var before = Environment.CurrentManagedThreadId;

        await new ResumeOnNewThread();

        Environment.CurrentManagedThreadId.Should().NotBe(before);
        _currentTenant.Id.Should().Be("tenant-a");
        _currentTenant.Name.Should().Be("Tenant A");
    }

    [Fact]
    public async Task should_not_leak_a_tenant_set_inside_an_awaited_method_back_to_the_caller()
    {
        using var _ = _currentTenant.Change("outer");

        var seenInside = await _SetTenantAndResumeElsewhereAsync("inner");

        seenInside.Should().Be("inner");
        _currentTenant.Id.Should().Be("outer");
    }

    private async Task<string?> _SetTenantAndResumeElsewhereAsync(string tenantId)
    {
        AsyncLocalCurrentTenantAccessor.Instance.Current = new TenantInformation(tenantId);

        await new ResumeOnNewThread();

        return _currentTenant.Id;
    }

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

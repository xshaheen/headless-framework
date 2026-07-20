// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging.Pulsar;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class PulsarConsumerClientLifecycleTests : TestBase
{
    [Fact]
    public async Task should_keep_replacement_receive_token_active_when_pause_and_resume_overlap()
    {
        // given
        await using var client = new PulsarConsumerClient(
            Options.Create(new PulsarMessagingOptions { ServiceUrl = "pulsar://localhost:6650" }),
            client: null!,
            groupName: "lifecycle-test",
            groupConcurrent: 0
        );
        var receiveLock = _GetField<Lock>(client, "_receiveLock");
        var pauseGate = _GetField<ConsumerPauseGate>(client, "_pauseGate");

        Task pauseTask;
        Task resumeTask;
        receiveLock.Enter();
        try
        {
            // Pause owns the transition lock and reaches the receive lock. Resume must not
            // replace the receive source until pause has cancelled the previous generation.
            pauseTask = Task.Run(async () => await client.PauseAsync(AbortToken), AbortToken);
            SpinWait.SpinUntil(() => pauseGate.IsPaused, TimeSpan.FromSeconds(5)).Should().BeTrue();

            // when
            resumeTask = client.ResumeAsync(AbortToken).AsTask();

            // then
            resumeTask.IsCompleted.Should().BeFalse("pause and resume transitions must be serialized");
        }
        finally
        {
            receiveLock.Exit();
        }

        await Task.WhenAll(pauseTask, resumeTask).WaitAsync(AbortToken);
        var receiveCts = _GetField<CancellationTokenSource>(client, "_receiveCts");
        pauseGate.IsPaused.Should().BeFalse();
        receiveCts.IsCancellationRequested.Should().BeFalse();
    }

    private static T _GetField<T>(object instance, string fieldName)
    {
#pragma warning disable REFL017 // The test intentionally inspects private lifecycle state to force the lock interleaving.
        return (T)
            instance
                .GetType()
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!
                .GetValue(instance)!;
#pragma warning restore REFL017
    }
}

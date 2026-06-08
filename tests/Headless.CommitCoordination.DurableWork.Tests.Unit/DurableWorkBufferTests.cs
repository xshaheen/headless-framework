// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.CommitCoordination.DurableWork;

namespace Tests;

public sealed class DurableWorkBufferTests
{
    [Fact]
    public async Task should_throw_by_default_when_relational_context_is_missing()
    {
        var buffer = new RecordingDurableWorkBuffer(new CommitCoordinator());

        var act = () => buffer.EnlistAsync("job-1", CancellationToken.None).AsTask();

        await act
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("Durable commit work requires IRelationalCommitContext.");
    }

    [Fact]
    public async Task should_allow_explicit_warn_fallback_when_relational_context_is_missing()
    {
        var buffer = new RecordingDurableWorkBuffer(
            new CommitCoordinator(),
            DurableWorkProviderMismatchPolicy.Warn
        );

        await buffer.EnlistAsync("job-1", CancellationToken.None);

        buffer.FallbackRows.Should().Equal(["job-1"]);
    }

    private sealed class RecordingDurableWorkBuffer(
        ICommitCoordinator coordinator,
        DurableWorkProviderMismatchPolicy policy = DurableWorkProviderMismatchPolicy.Throw
    ) : DurableWorkBuffer<string>(coordinator, policy)
    {
        public List<string> FallbackRows { get; } = [];

        protected override ValueTask WriteRowAsync(
            string row,
            IRelationalCommitContext relationalContext,
            CancellationToken cancellationToken
        )
        {
            return ValueTask.CompletedTask;
        }

        protected override ValueTask EnlistWithoutRelationalContextAsync(
            string row,
            CancellationToken cancellationToken
        )
        {
            FallbackRows.Add(row);

            return ValueTask.CompletedTask;
        }
    }
}

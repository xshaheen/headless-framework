// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.CommitCoordination.DurableWork;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class DurableWorkBufferTests
{
    [Fact]
    public async Task should_throw_by_default_when_relational_context_is_missing()
    {
        var buffer = new RecordingDurableWorkBuffer(new CommitCoordinator());

        var act = () => buffer.EnlistAsync("job-1", CancellationToken.None).AsTask();

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("Durable commit work requires IRelationalCommitContext.");
    }

    [Fact]
    public async Task should_allow_explicit_warn_fallback_when_relational_context_is_missing()
    {
        var logger = new RecordingLogger();
        var buffer = new RecordingDurableWorkBuffer(
            new CommitCoordinator(),
            DurableWorkProviderMismatchPolicy.Warn,
            logger
        );

        await buffer.EnlistAsync("job-1", CancellationToken.None);

        buffer.FallbackRows.Should().Equal(["job-1"]);
        logger.Warnings.Should().ContainSingle();
    }

    [Fact]
    public async Task should_throw_from_base_fallback_under_warn_when_fallback_not_overridden()
    {
        // Warn is only safe when a derived buffer supplies a genuinely durable fallback. A buffer that opts into
        // Warn but does NOT override EnlistWithoutRelationalContextAsync must fail closed — the base must not
        // silently drop the row, which would void the "no work lost" floor.
        var buffer = new NonOverridingDurableWorkBuffer(
            new CommitCoordinator(),
            DurableWorkProviderMismatchPolicy.Warn
        );

        var act = () => buffer.EnlistAsync("job-1", CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not override*");
    }

    // Warn policy but no fallback override — the base EnlistWithoutRelationalContextAsync throw must fire.
    private sealed class NonOverridingDurableWorkBuffer(
        ICommitCoordinator coordinator,
        DurableWorkProviderMismatchPolicy policy
    ) : DurableWorkBuffer<string>(coordinator, policy)
    {
        protected override ValueTask WriteRowAsync(
            string row,
            IRelationalCommitContext relationalContext,
            CancellationToken cancellationToken
        )
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingDurableWorkBuffer(
        ICommitCoordinator coordinator,
        DurableWorkProviderMismatchPolicy policy = DurableWorkProviderMismatchPolicy.Throw,
        ILogger? logger = null
    ) : DurableWorkBuffer<string>(coordinator, policy, logger)
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

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}

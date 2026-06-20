// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination;

/// <summary>
/// Maintains the ambient commit coordinator stack for the current async flow. Core-internal infrastructure:
/// resolve <see cref="ICurrentCommitCoordinator" /> instead.
/// </summary>
internal sealed class CommitScopeStack : ICurrentCommitCoordinator
{
    private static readonly AsyncLocal<CommitScopeFrame?> _current = new();

    public ICommitCoordinator? Current => _current.Value?.Coordinator;

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "This property is intentionally instance-shaped for DI consumers."
    )]
    internal CommitCoordinator? CurrentCore => _current.Value?.Coordinator;

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "This method is intentionally instance-shaped for the registered stack service."
    )]
    internal IDisposable Push(CommitCoordinator coordinator)
    {
        var frame = new CommitScopeFrame(coordinator, _current.Value);
        _current.Value = frame;

        return new PopHandle(frame);
    }

    private sealed record CommitScopeFrame(CommitCoordinator Coordinator, CommitScopeFrame? Parent);

    private sealed class PopHandle(CommitScopeFrame frame) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (!ReferenceEquals(_current.Value, frame))
            {
                if (ReferenceEquals(_current.Value, frame.Parent))
                {
                    _disposed = true;
                    return;
                }

                if (_Contains(_current.Value, frame))
                {
                    throw new InvalidOperationException("Commit scope disposed out of order.");
                }

                _disposed = true;
                return;
            }

            _current.Value = frame.Parent;
            _disposed = true;
        }

        private static bool _Contains(CommitScopeFrame? current, CommitScopeFrame frame)
        {
            while (current is not null)
            {
                if (ReferenceEquals(current, frame))
                {
                    return true;
                }

                current = current.Parent;
            }

            return false;
        }
    }
}

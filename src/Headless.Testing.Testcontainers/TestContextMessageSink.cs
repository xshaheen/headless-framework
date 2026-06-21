// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Xunit;
using Xunit.Sdk;

namespace Headless.Testing.Testcontainers;

/// <summary>
/// xUnit v3 <see cref="IMessageSink"/> that forwards Testcontainers diagnostic
/// messages to the ambient <see cref="TestContext.Current"/> so they appear in
/// the test runner output.
/// </summary>
[PublicAPI]
public sealed class TestContextMessageSink : IMessageSink
{
    /// <summary>Singleton instance suitable for sharing across fixtures.</summary>
    public static readonly TestContextMessageSink Instance = new();

    private TestContextMessageSink() { }

    /// <summary>
    /// Forwards <paramref name="message"/> as a diagnostic message on <see cref="TestContext.Current"/>.
    /// Always returns <see langword="true"/> to continue receiving messages.
    /// </summary>
    /// <param name="message">The Testcontainers message to forward.</param>
    /// <returns><see langword="true"/> always.</returns>
    public bool OnMessage(IMessageSinkMessage message)
    {
        TestContext.Current.SendDiagnosticMessage(message.ToJson() ?? message.ToString() ?? string.Empty);

        return true;
    }
}

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

    public bool OnMessage(IMessageSinkMessage message)
    {
        TestContext.Current.SendDiagnosticMessage(message.ToJson() ?? message.ToString() ?? string.Empty);

        return true;
    }
}

// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Xunit;
using Xunit.Sdk;

namespace Framework.Testing.Testcontainers;

internal sealed class TestContextMessageSink : IMessageSink
{
    public static readonly TestContextMessageSink Instance = new();

    private TestContextMessageSink() { }

    public bool OnMessage(IMessageSinkMessage message)
    {
        TestContext.Current.SendDiagnosticMessage(message.ToJson() ?? message.ToString() ?? string.Empty);

        return true;
    }
}

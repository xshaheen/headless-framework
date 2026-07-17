namespace Demo.Messages;

public class TestMessage
{
    public static TestMessage Create(string text)
    {
        return new() { Text = text };
    }

    public string Text { get; private init; } = null!;
}

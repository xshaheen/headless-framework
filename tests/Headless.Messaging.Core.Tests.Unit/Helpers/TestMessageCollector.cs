namespace Tests.Helpers;

public sealed class TestMessageCollector(ICollection<object> handledMessages)
{
    public void Add(object data)
    {
        handledMessages.Add(data);
    }
}

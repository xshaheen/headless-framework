namespace Tests.Helpers;

public class TestMessageCollector(ICollection<object> handledMessages)
{
    public void Add(object data)
    {
        handledMessages.Add(data);
    }
}

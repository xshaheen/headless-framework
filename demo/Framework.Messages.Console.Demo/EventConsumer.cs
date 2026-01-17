using Framework.Messages;

namespace Demo;

public class EventConsumer : IConsumer
{
    [CapSubscribe("sample.console.showtime")]
    public async Task ShowTime(DateTime date)
    {
        Console.WriteLine(date);
        await Task.CompletedTask;
    }
}

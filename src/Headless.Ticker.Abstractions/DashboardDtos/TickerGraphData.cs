namespace Headless.Ticker.DashboardDtos;

public class TickerGraphData
{
    public DateTime Date { get; set; }
    public required Tuple<int, int>[] Results { get; set; }
}

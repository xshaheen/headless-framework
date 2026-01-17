using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("TickerQ")]
[assembly: InternalsVisibleTo("Framework.Ticker.EntityFrameworkCore")]
[assembly: InternalsVisibleTo("Framework.Ticker.Dashboard")]
[assembly: InternalsVisibleTo("Framework.Ticker.Tests.Unit")]
[assembly: InternalsVisibleTo("Framework.Ticker.Instrumentation.OpenTelemetry")]
[assembly: InternalsVisibleTo("Framework.Ticker.Caching.StackExchangeRedis")]
// To be testable using NSubsitute
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]

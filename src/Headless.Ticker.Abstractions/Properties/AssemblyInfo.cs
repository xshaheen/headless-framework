using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("TickerQ")]
[assembly: InternalsVisibleTo("Headless.Ticker.EntityFrameworkCore")]
[assembly: InternalsVisibleTo("Headless.Ticker.Dashboard")]
[assembly: InternalsVisibleTo("Headless.Ticker.Tests.Unit")]
[assembly: InternalsVisibleTo("Headless.Ticker.Instrumentation.OpenTelemetry")]
[assembly: InternalsVisibleTo("Headless.Ticker.Caching.StackExchangeRedis")]
// To be testable using NSubsitute
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]

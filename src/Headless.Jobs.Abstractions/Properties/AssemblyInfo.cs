using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Jobs")]
[assembly: InternalsVisibleTo("Headless.Jobs.EntityFrameworkCore")]
[assembly: InternalsVisibleTo("Headless.Jobs.Dashboard")]
[assembly: InternalsVisibleTo("Headless.Jobs.Tests.Unit")]
[assembly: InternalsVisibleTo("Headless.Jobs.Instrumentation.OpenTelemetry")]
[assembly: InternalsVisibleTo("Headless.Jobs.Caching.StackExchangeRedis")]
// To be testable using NSubsitute
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]

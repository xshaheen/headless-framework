using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Headless.Jobs.Core")]
[assembly: InternalsVisibleTo("Headless.Jobs.EntityFramework")]
[assembly: InternalsVisibleTo("Headless.Jobs.Dashboard")]
[assembly: InternalsVisibleTo("Headless.Jobs.Tests.Unit")]
[assembly: InternalsVisibleTo("Headless.Jobs.OpenTelemetry")]
// To be testable using NSubsitute
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]

// Copyright (c) Mahmoud Shaheen. All rights reserved.

// The context every guide example compiles in. It is never built into the test assembly: the test parses it next to
// each example. Keep it to what a reader would assume without being told: the usings an ASP.NET Core project imports
// implicitly, and the host objects every setup snippet starts from. A guide's own prelude adds the namespaces of the
// Headless packages it documents; any other namespace (EF Core, Polly, OpenTelemetry, a broker client) is written in
// the example itself, as a reader copying it would need it.

global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Net.Http;
global using System.Net.Http.Json;
global using System.Threading;
global using System.Threading.Tasks;
global using DocsPrelude;
global using Microsoft.AspNetCore.Builder;
global using Microsoft.AspNetCore.Hosting;
global using Microsoft.AspNetCore.Http;
global using Microsoft.AspNetCore.Routing;
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace DocsPrelude
{
    /// <summary>
    /// The names an example may use without declaring them. Statements and loose members of an example run inside a
    /// class derived from this one.
    /// </summary>
    public abstract partial class Ambient
    {
        protected string[] args = [];
        protected WebApplicationBuilder builder = null!;
        protected IServiceCollection services = null!;
        protected IConfiguration configuration = null!;
        protected WebApplication app = null!;
        protected IServiceProvider provider = null!;
        protected IServiceScope scope = null!;
        protected TimeProvider timeProvider = null!;
        protected CancellationToken cancellationToken;
        protected CancellationToken ct;
        protected string connectionString = "";
        protected string tenantId = "";
    }

    /// <summary>Stands in for the application's own <c>DbContext</c>.</summary>
    public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);

    /// <summary>Stands in for a top-level-statements host's entry point, as in <c>ILogger&lt;Program&gt;</c>.</summary>
    public sealed class Program;
}

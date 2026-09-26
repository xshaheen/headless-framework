// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Compiled with every documentation example. The usings match the ASP.NET Core SDK's implicit usings, which a
// consuming web project already has. The ambient members stand in for the variables an example assumes from its
// surrounding program; a local of the same name in the example shadows them.

global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Net.Http;
global using System.Net.Http.Json;
global using System.Threading;
global using System.Threading.Tasks;
// Test examples assert with the repository's assertion library.
global using AwesomeAssertions;
global using static DocsAmbient;
// Example declarations without a namespace are compiled into this one.
global using DocsExample;
global using Microsoft.AspNetCore.Builder;
global using Microsoft.AspNetCore.Hosting;
global using Microsoft.AspNetCore.Http;
global using Microsoft.AspNetCore.Routing;
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;

#pragma warning disable IDE1006 // Ambient members mirror the camelCase locals the examples use.
public static class DocsAmbient
{
    public static string[] args => [];

    public static WebApplicationBuilder builder => null!;

    public static IServiceCollection services => null!;

    public static IConfiguration configuration => null!;

    public static IHostEnvironment environment => null!;

    public static WebApplication app => null!;

    public static IServiceProvider serviceProvider => null!;

    public static IServiceProvider provider => null!;

    public static CancellationToken cancellationToken => default;

    public static CancellationToken ct => default;
}

namespace DocsExample { }

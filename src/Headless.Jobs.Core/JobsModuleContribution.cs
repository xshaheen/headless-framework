// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs;

/// <summary>One generated module that a contribution or the host asked the host's catalog to register.</summary>
/// <param name="ModuleType">The generated module type, which identifies the module across contributions.</param>
/// <param name="Register">Runs the module's generated registration against one host's catalog.</param>
internal sealed record JobsModuleContribution(Type ModuleType, Action<JobsCatalogBuilder> Register);

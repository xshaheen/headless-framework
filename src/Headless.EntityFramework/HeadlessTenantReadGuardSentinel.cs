// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.AuditLog;
using Headless.Checks;
using Headless.Domain;
using Headless.EntityFramework.CompiledQueryCache;
using Headless.EntityFramework.Contexts.Runtime;
using Headless.MultiTenancy;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.EntityFramework;

/// <summary>Sentinel marker for one-shot tenant-read-guard PostConfigure registration.</summary>
internal sealed class HeadlessTenantReadGuardSentinel;

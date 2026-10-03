// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Cors;
using Headless.Checks;
using Headless.Constants;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Api;

internal sealed class HeadlessCorsPolicyProviderMarker;

// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using System.Globalization;
using Headless.Checks;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;
using Headless.Reliability;
using Microsoft.Extensions.Configuration;

namespace Headless.Jobs;

/// <summary>One job's <c>FailurePolicy</c> configuration section, applied after its policy is otherwise resolved.</summary>
internal readonly record struct ConfiguredFailurePolicy(string Identity, string Path, FailurePolicyOverrides Overrides);

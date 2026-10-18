// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Hosting;

/// <summary>
/// What an application can change about how its stateful resources are managed. Configure it with
/// <c>services.ConfigureHeadlessStatefulResources(options =&gt; ...)</c>.
/// </summary>
[PublicAPI]
public sealed class StatefulResourceOptions
{
    /// <summary>
    /// The environments where the operations that delete data (<c>resources clear</c>, <c>resources teardown</c>,
    /// <see cref="StatefulResourceStartupAction.ResetState" />, <c>ResetResourceStateAsync</c>, and
    /// <c>TeardownResourcesAsync</c>) run without <c>--force</c>. Compared case-insensitively. Every other environment
    /// refuses them, so a production-like environment with any name (<c>Prod</c>, <c>Live</c>, <c>Staging</c>) is safe by
    /// default. Default: <c>Development</c> and <c>Test</c>.
    /// </summary>
    public List<string> DestructiveEnvironments { get; set; } = [EnvironmentNames.Development, EnvironmentNames.Test];
}

internal sealed class StatefulResourceOptionsValidator : AbstractValidator<StatefulResourceOptions>
{
    public StatefulResourceOptionsValidator()
    {
        RuleFor(x => x.DestructiveEnvironments).NotNull();
        RuleForEach(x => x.DestructiveEnvironments).NotEmpty();
    }
}

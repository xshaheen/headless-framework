// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Abstractions;

/// <summary>
/// Provides identity information about the running application instance.
/// Useful for multi-application deployments where resources must be attributed to a specific service.
/// </summary>
public interface IApplicationInformationAccessor
{
    /// <summary>
    /// Name of the application. This is useful for systems with multiple applications, to distinguish
    /// resources of the applications located together.
    /// </summary>
    string ApplicationName { get; }

    /// <summary>
    /// A unique identifier for this application instance. This value changes whenever the application is restarted.
    /// </summary>
    string InstanceId { get; }
}

/// <summary>
/// An <see cref="IApplicationInformationAccessor"/> that uses a compile-time or configuration-supplied
/// application name rather than reading assembly attributes.
/// </summary>
/// <param name="name">The application name. Must not be null, empty, or whitespace.</param>
/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
/// <exception cref="ArgumentException"><paramref name="name"/> is empty or whitespace.</exception>
public sealed class StaticApplicationInformationAccessor(string name) : IApplicationInformationAccessor
{
    /// <inheritdoc/>
    public string ApplicationName { get; } = Argument.IsNotNullOrWhiteSpace(name);

    /// <inheritdoc/>
    public string InstanceId { get; } = Guid.NewGuid().ToString();
}

/// <summary>
/// An <see cref="IApplicationInformationAccessor"/> that derives the application name from the entry
/// assembly's <see cref="IBuildInformationAccessor.GetTitle"/> value, falling back to <c>"Unknown"</c>
/// when the title is not declared.
/// </summary>
/// <param name="accessor">The build information source used to read the assembly title.</param>
public sealed class ApplicationInformationAccessor(IBuildInformationAccessor accessor) : IApplicationInformationAccessor
{
    /// <inheritdoc/>
    public string ApplicationName { get; } = accessor.GetTitle() ?? "Unknown";

    /// <inheritdoc/>
    public string InstanceId { get; } = Guid.NewGuid().ToString();
}

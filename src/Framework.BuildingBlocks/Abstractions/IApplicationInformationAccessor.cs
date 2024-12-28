// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Abstractions;

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

public sealed class StaticApplicationInformationAccessor(string name) : IApplicationInformationAccessor
{
    public string ApplicationName { get; } = name;

    public string InstanceId { get; } = Guid.NewGuid().ToString();
}

public sealed class ApplicationInformationAccessor(IBuildInformationAccessor accessor) : IApplicationInformationAccessor
{
    public string ApplicationName { get; } = accessor.GetTitle() ?? "Unknown";

    public string InstanceId { get; } = Guid.NewGuid().ToString();
}

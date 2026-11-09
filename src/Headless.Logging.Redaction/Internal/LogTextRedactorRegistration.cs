// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Logging.Internal;

/// <summary>A text redactor and the entries it scans.</summary>
internal sealed record LogTextRedactorRegistration(
    ILogTextRedactor Redactor,
    LogLevel MinimumLevel,
    IReadOnlyList<string> Categories
)
{
    /// <summary>
    /// The builder's registrations, plus every <see cref="ILogTextRedactor"/> registered directly in the container, which
    /// scans every entry.
    /// </summary>
    public static LogTextRedactorRegistration[] All(IServiceProvider provider)
    {
        return
        [
            .. provider
                .GetServices<ILogTextRedactor>()
                .Select(static r => new LogTextRedactorRegistration(r, LogLevel.Trace, [])),
            .. provider.GetServices<LogTextRedactorRegistration>(),
        ];
    }

    public bool AppliesTo(string categoryName)
    {
        if (Categories.Count == 0)
        {
            return true;
        }

        foreach (var prefix in Categories)
        {
            if (categoryName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

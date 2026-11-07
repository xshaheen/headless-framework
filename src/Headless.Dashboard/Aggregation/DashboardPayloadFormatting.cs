// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Checks;

namespace Headless.Dashboard;

/// <summary>
/// Passes an object's payload through its module's <see cref="IDashboardPayloadFormatter"/> before the payload leaves
/// the server, so redaction never depends on the browser.
/// </summary>
internal sealed class DashboardPayloadFormatting(DashboardModuleCatalog catalog)
{
    /// <summary>
    /// Returns <paramref name="payload"/> as <paramref name="user"/> may see it: formatted by the owning module's
    /// formatter when it handles the object's kind, otherwise as it is.
    /// </summary>
    /// <remarks>
    /// A refused module's formatter still runs: refusing a module withholds its contributions, not the redaction its
    /// data needs.
    /// </remarks>
    public ValueTask<DashboardPayload> FormatAsync(
        ObjectRef target,
        DashboardPayload payload,
        ClaimsPrincipal user,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(target);
        Argument.IsNotNull(payload);
        Argument.IsNotNull(user);

        var formatter = _FindFormatter(target);

        if (formatter is null)
        {
            return ValueTask.FromResult(payload);
        }

        return formatter.FormatAsync(
            new DashboardPayloadFormatRequest
            {
                Target = target,
                Payload = payload,
                User = user,
            },
            cancellationToken
        );
    }

    private IDashboardPayloadFormatter? _FindFormatter(ObjectRef target)
    {
        foreach (var module in catalog.Modules)
        {
            if (
                string.Equals(module.Id, target.Module, StringComparison.Ordinal)
                && module is IDashboardPayloadFormatter formatter
                && formatter.ObjectKinds.Contains(target.Kind, StringComparer.Ordinal)
            )
            {
                return formatter;
            }
        }

        return null;
    }
}

// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Microsoft.Extensions.Options;

namespace Headless.AuditLog;

/// <summary>
/// Fails startup when <see cref="MissingTransactionStrategy.Throw"/> is set but no
/// <see cref="IAmbientDbTransactionAccessor"/> is registered, because every relational audit write would then throw.
/// </summary>
internal sealed class RelationalAuditLogEnlistmentStartupValidator(
    IOptions<AuditLogOptions> options,
    RelationalAuditLogTable table,
    IAmbientDbTransactionAccessor? ambientTransactionAccessor = null
) : IHeadlessStartupValidator
{
    public Task ValidateAsync(CancellationToken cancellationToken)
    {
        if (
            options.Value.MissingTransactionStrategy == MissingTransactionStrategy.Throw
            && ambientTransactionAccessor is null
        )
        {
            throw new InvalidOperationException(
                $"Headless.AuditLog: {nameof(AuditLogOptions.MissingTransactionStrategy)} is {nameof(MissingTransactionStrategy.Throw)}, "
                    + $"but no {nameof(IAmbientDbTransactionAccessor)} is registered, so every {table.Dialect.DisplayName} audit write "
                    + "would fail. Register the Headless EF Core services (AddHeadlessDbContext) or an accessor of your own, "
                    + $"or set {nameof(AuditLogOptions.MissingTransactionStrategy)} to {nameof(MissingTransactionStrategy.Continue)}."
            );
        }

        return Task.CompletedTask;
    }
}

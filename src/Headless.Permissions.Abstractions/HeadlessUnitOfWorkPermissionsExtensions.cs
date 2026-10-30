// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Permissions;

namespace Headless.UnitOfWork;

/// <summary>
/// Exposes the enlisted permission grant writes on <see cref="IUnitOfWork" />, so a grant or revoke commits or rolls
/// back with the caller's business writes.
/// </summary>
[PublicAPI]
public static class HeadlessUnitOfWorkPermissionsExtensions
{
    private const string _NotRegisteredMessage =
        "No permissions storage is registered for this unit of work. Call AddHeadlessPermissions during startup with "
        + "UseEntityFramework, UsePostgreSql, or UseSqlServer.";

    extension(IUnitOfWork unitOfWork)
    {
        /// <summary>
        /// Gets the permission grant writes bound to this handle: a grant, denial, or delete made through them runs
        /// inside this unit's transaction, and the grant cache is cleared only after the unit commits.
        /// </summary>
        /// <remarks>
        /// Free to read at each call site: the binding is created once per unit, on the first read, and kept as
        /// unit-local state. Reading it on a unit that already completed or rolled back throws.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="unitOfWork"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// No permissions storage is registered, or <paramref name="unitOfWork"/> is no longer active.
        /// </exception>
        /// <exception cref="ObjectDisposedException"><paramref name="unitOfWork"/> is disposed.</exception>
        public UnitOfWorkPermissions Permissions
        {
            get
            {
                Argument.IsNotNull(unitOfWork);

                var feature =
                    unitOfWork.GetFeature<IUnitOfWorkPermissions>()
                    ?? throw new InvalidOperationException(_NotRegisteredMessage);

                return unitOfWork.GetOrAdd(
                    feature,
                    static (unit, permissions) => new UnitOfWorkPermissions(permissions, unit)
                );
            }
        }
    }
}

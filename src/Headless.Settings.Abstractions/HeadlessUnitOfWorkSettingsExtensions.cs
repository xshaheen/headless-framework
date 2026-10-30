// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Settings;

namespace Headless.UnitOfWork;

/// <summary>
/// Exposes the enlisted setting writes on <see cref="IUnitOfWork" />, so a setting change commits or rolls back with
/// the caller's business writes.
/// </summary>
[PublicAPI]
public static class HeadlessUnitOfWorkSettingsExtensions
{
    private const string _NotRegisteredMessage =
        "No settings storage is registered for this unit of work. Call AddHeadlessSettings during startup with "
        + "UseEntityFramework, UsePostgreSql, or UseSqlServer.";

    extension(IUnitOfWork unitOfWork)
    {
        /// <summary>
        /// Gets the setting writes bound to this handle: a value written or deleted through them runs inside this
        /// unit's transaction, and the value cache is cleared only after the unit commits.
        /// </summary>
        /// <remarks>
        /// Free to read at each call site: the binding is created once per unit, on the first read, and kept as
        /// unit-local state. Reading it on a unit that already completed or rolled back throws.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="unitOfWork"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// No settings storage is registered, or <paramref name="unitOfWork"/> is no longer active.
        /// </exception>
        /// <exception cref="ObjectDisposedException"><paramref name="unitOfWork"/> is disposed.</exception>
        public UnitOfWorkSettings Settings
        {
            get
            {
                Argument.IsNotNull(unitOfWork);

                var feature =
                    unitOfWork.GetFeature<IUnitOfWorkSettings>()
                    ?? throw new InvalidOperationException(_NotRegisteredMessage);

                return unitOfWork.GetOrAdd(feature, static (unit, settings) => new UnitOfWorkSettings(settings, unit));
            }
        }
    }
}

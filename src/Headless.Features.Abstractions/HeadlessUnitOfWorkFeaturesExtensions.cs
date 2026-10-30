// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Features;

namespace Headless.UnitOfWork;

/// <summary>
/// Exposes the enlisted feature value writes on <see cref="IUnitOfWork" />, so a feature value change commits or rolls back with
/// the caller's business writes.
/// </summary>
[PublicAPI]
public static class HeadlessUnitOfWorkFeaturesExtensions
{
    private const string _NotRegisteredMessage =
        "No features storage is registered for this unit of work. Call AddHeadlessFeatures during startup with "
        + "UseEntityFramework, UsePostgreSql, or UseSqlServer.";

    extension(IUnitOfWork unitOfWork)
    {
        /// <summary>
        /// Gets the feature value writes bound to this handle: a value written or deleted through them runs inside this
        /// unit's transaction, and the value cache is cleared only after the unit commits.
        /// </summary>
        /// <remarks>
        /// Free to read at each call site: the binding is created once per unit, on the first read, and kept as
        /// unit-local state. Reading it on a unit that already completed or rolled back throws.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="unitOfWork"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// No features storage is registered, or <paramref name="unitOfWork"/> is no longer active.
        /// </exception>
        /// <exception cref="ObjectDisposedException"><paramref name="unitOfWork"/> is disposed.</exception>
        public UnitOfWorkFeatures Features
        {
            get
            {
                Argument.IsNotNull(unitOfWork);

                var feature =
                    unitOfWork.GetFeature<IUnitOfWorkFeatures>()
                    ?? throw new InvalidOperationException(_NotRegisteredMessage);

                return unitOfWork.GetOrAdd(feature, static (unit, features) => new UnitOfWorkFeatures(features, unit));
            }
        }
    }
}

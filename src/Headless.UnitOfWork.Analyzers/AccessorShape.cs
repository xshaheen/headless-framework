// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.UnitOfWork.Analyzers;

/// <summary>How the enlisted receiver is reached from a unit of work.</summary>
internal enum AccessorShape
{
    /// <summary>An extension property, such as <c>unit.Outbox</c>.</summary>
    Property = 0,

    /// <summary>
    /// A generic extension method whose type argument comes from the autonomous receiver's first type argument, such
    /// as <c>unit.TimeJobs&lt;TJob&gt;()</c> for an <c>ITimeJobManager&lt;TJob&gt;</c>.
    /// </summary>
    GenericMethod = 1,
}

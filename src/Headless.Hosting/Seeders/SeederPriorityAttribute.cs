// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting.Seeders;

/// <summary>
/// Specifies the execution priority for an <see cref="ISeeder"/> implementation.
/// </summary>
/// <remarks>
/// Seeders are sorted ascending by this value before execution, so lower numbers run first.
/// Seeders without this attribute default to priority <c>0</c>. Migration seeders conventionally
/// use <see cref="int.MinValue"/> to guarantee they run before all data seeders.
/// </remarks>
/// <param name="priority">The execution priority. Lower values run first.</param>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class)]
public sealed class SeederPriorityAttribute(int priority) : Attribute
{
    /// <summary>Gets the execution priority. Lower values run first.</summary>
    public int Priority { get; } = priority;
}

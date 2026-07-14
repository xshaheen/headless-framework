// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Base;

/// <summary>
/// Marks the constructor that the Jobs source generator should use to instantiate the job class via
/// dependency injection when the class has multiple constructors.
/// </summary>
/// <remarks>
/// The source generator prefers the constructor annotated with this attribute over the first public
/// constructor. Omit it when the class has only one constructor, or when the first public constructor
/// should be used. Placing this attribute on more than one constructor in the same class is a compile-time
/// error (diagnostic HF010).
/// </remarks>
[AttributeUsage(AttributeTargets.Constructor)]
public sealed class JobsConstructorAttribute : Attribute;

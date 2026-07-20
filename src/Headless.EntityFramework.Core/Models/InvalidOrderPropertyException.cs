// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.EntityFramework;

/// <summary>
/// Thrown when a sort property name passed to a string-based ordering extension does not correspond
/// to a valid property path on the entity type.
/// </summary>
public sealed class InvalidOrderPropertyException(string message, Exception? inner) : Exception(message, inner);

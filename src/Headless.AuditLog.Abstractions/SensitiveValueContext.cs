// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog;

/// <summary>
/// Provides context to <see cref="AuditLogOptions.SensitiveValueTransformer"/>
/// for transforming a sensitive property value.
/// </summary>
/// <param name="EntityType">Full CLR type name of the entity.</param>
/// <param name="PropertyName">Name of the sensitive property.</param>
/// <param name="PropertyClrType">CLR type of the property.</param>
/// <param name="Value">The raw property value to transform.</param>
public readonly record struct SensitiveValueContext(
    string EntityType,
    string PropertyName,
    Type PropertyClrType,
    object? Value
);

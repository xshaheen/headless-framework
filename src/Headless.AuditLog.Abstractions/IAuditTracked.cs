// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog;

/// <summary>
/// Marker interface. Entities implementing this will have property-level
/// changes captured automatically by the EF Core ChangeTracker during SaveChanges.
/// </summary>
public interface IAuditTracked;

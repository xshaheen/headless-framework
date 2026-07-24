// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Entities.BaseEntity;

/// <summary>
/// Base entity shared by <c>TimeJobEntity</c> and <c>CronJobEntity</c>. Carries the identity,
/// function binding, and audit timestamps common to all job types.
/// </summary>
[PublicAPI]
public class BaseJobEntity
{
    /// <summary>
    /// Unique identifier for this job row. The Jobs manager assigns an <c>IGuidGenerator</c> value when this is empty.
    /// </summary>
    public virtual Guid Id { get; set; }

    /// <summary>
    /// The registered function name that binds this job to its handler delegate. Must match a name
    /// registered via <c>JobFunctionAttribute</c>.
    /// </summary>
    public virtual string Function { get; set; } = null!;

    /// <summary>
    /// Optional human-readable description of this job instance, used for display in the dashboard.
    /// <see langword="null"/> when no description was supplied.
    /// </summary>
    public virtual string? Description { get; set; }

    /// <summary>
    /// Optional identifier set at seeding time to correlate this row with its code-defined seed entry.
    /// Used by the startup seeder to detect and update existing seed rows instead of inserting duplicates.
    /// </summary>
    public virtual string? InitIdentifier { get; internal set; }

    /// <summary>
    /// Tenant that owns this job, resolved at schedule time (explicit value wins, otherwise ambient capture when
    /// propagation is enabled). <see langword="null"/> means system scope. Cron definitions and occurrences are
    /// always system scope and reject a non-null value.
    /// </summary>
    public virtual string? TenantId { get; set; }

    /// <summary>
    /// Transient schedule-time flag marking a deliberate system-scope (tenantless) job. Rejected when an ambient
    /// tenant is present so tenant code cannot escalate into system scope. Never persisted.
    /// </summary>
    public virtual bool IsSystemJob { get; set; }

    /// <summary>UTC timestamp when this row was first persisted.</summary>
    public virtual DateTime DateCreated { get; internal set; }

    /// <summary>UTC timestamp of the most recent update to this row.</summary>
    public virtual DateTime DateUpdated { get; internal set; }
}

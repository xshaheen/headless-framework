namespace Headless.Jobs.Entities.BaseEntity;

public class BaseJobEntity
{
    public virtual Guid Id { get; set; } = Guid.NewGuid();
    public virtual string Function { get; set; } = null!;
    public virtual string Description { get; set; } = null!;
    public virtual string? InitIdentifier { get; internal set; }
    public virtual DateTime CreatedAt { get; internal set; } = DateTime.UtcNow;
    public virtual DateTime UpdatedAt { get; internal set; } = DateTime.UtcNow;
}

namespace Framework.Orm.EntityFramework.AuditLogs;

[AttributeUsage(AttributeTargets.Class)]
public sealed class AuditAttribute : Attribute;

[AttributeUsage(AttributeTargets.Property)]
public sealed class TrackAttribute(bool keepAlways = false) : Attribute
{
    public bool KeepAlways { get; } = keepAlways;
}

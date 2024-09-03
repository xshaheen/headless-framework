// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Domains;

public interface IHasConcurrencyStamp
{
    string? ConcurrencyStamp { get; set; }
}

// ReSharper disable once CheckNamespace
namespace Framework.BuildingBlocks.Domains;

public interface IHasConcurrencyStamp
{
    string? ConcurrencyStamp { get; set; }
}

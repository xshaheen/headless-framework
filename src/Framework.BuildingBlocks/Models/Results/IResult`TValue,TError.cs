#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.BuildingBlocks;

public interface IResult<out TValue, out TError> : IResult<TError>
{
    public TResult Match<TResult>(Func<TValue, TResult> success, Func<TError, TResult> failure);
}

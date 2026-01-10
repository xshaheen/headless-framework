using MediatR;

namespace Framework.Domain.Queries;

/// <summary>
/// Query marker interface. Compatible with MediatR.IRequest{TResult}.
/// </summary>
public interface IQuery<out TResult> : IRequest<TResult>;

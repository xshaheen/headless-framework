using MediatR;

namespace Framework.Domain.Queries;

/// <summary>
/// Query handler. Compatible with MediatR.IRequestHandler.
/// </summary>
public interface IQueryHandler<TQuery, TResult> : IRequestHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>;

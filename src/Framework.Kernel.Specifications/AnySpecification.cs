using System.Linq.Expressions;

namespace Framework.Kernel.Specifications;

/// <summary>Represents the specification that can be satisfied by the given object in any circumstance.</summary>
/// <typeparam name="T">The type of the object to which the specification is applied.</typeparam>
public sealed class AnySpecification<T> : Specification<T>
{
    public override Expression<Func<T, bool>> ToExpression()
    {
        return o => true;
    }
}

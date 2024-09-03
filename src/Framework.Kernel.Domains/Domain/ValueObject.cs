// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Domains;

public interface IValueObject;

public abstract class ValueObject : EquatableBase<ValueObject>, IValueObject;

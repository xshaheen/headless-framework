using Framework.BuildingBlocks.Domains.Helpers;

// ReSharper disable once CheckNamespace
namespace Framework.BuildingBlocks.Domains;

public interface IValueObject;

public abstract class ValueObject : EquatableBase<ValueObject>, IValueObject;

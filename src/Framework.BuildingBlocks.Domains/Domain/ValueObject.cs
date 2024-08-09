// ReSharper disable once CheckNamespace

using Framework.BuildingBlocks.Domains.Helpers;

namespace Framework.BuildingBlocks.Domains;

public interface IValueObject;

public abstract class ValueObject : EquatableBase<ValueObject>, IValueObject;

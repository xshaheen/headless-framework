// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Generator.Primitives.Helpers;

public static class DapperTypeHandlerHelper
{
    public static string CreateStringDapperHandler(string name, string dapperTypeHandler)
    {
        return $$"""
            public sealed class {{dapperTypeHandler}} : global::Dapper.SqlMapper.TypeHandler<{{name}}>
            {
                public override void SetValue(global::System.Data.IDbDataParameter parameter, {{name}} value)
                {
                    parameter.Value = value?.GetUnderlyingPrimitiveType();
                }

                public override {{name}} Parse(object value)
                {
                    return value switch
                    {
                        string stringValue => new {{name}}(stringValue),
                        _ => throw new global::System.InvalidCastException($"Unable to cast object of type {value.GetType()} to {{name}}"),
                    };
                }
            }
            """;
    }

    public static string CreateGuidDapperHandler(string name, string dapperTypeHandler)
    {
        return $$"""
            public sealed class {{dapperTypeHandler}} : global::Dapper.SqlMapper.TypeHandler<{{name}}>
            {
                public override void SetValue(global::System.Data.IDbDataParameter parameter, {{name}} value)
                {
                    parameter.Value = value?.GetUnderlyingPrimitiveType();
                }

                public override {{name}} Parse(object value)
                {
                    return value switch
                    {
                        global::System.Guid guidValue => new {{name}}(guidValue),
                        string stringValue when !string.IsNullOrEmpty(stringValue) && global::System.Guid.TryParse(stringValue, out var result) => new {{name}}(result),
                        _ => throw new global::System.InvalidCastException($"Unable to cast object of type {value.GetType()} to {{name}}"),
                    };
                }
            }
            """;
    }

    public static string CreateIntDapperHandler(string name, string dapperTypeHandler)
    {
        return $$"""
            public sealed class {{dapperTypeHandler}} : global::Dapper.SqlMapper.TypeHandler<{{name}}>
            {
                public override void SetValue(global::System.Data.IDbDataParameter parameter, {{name}} value)
                {
                    parameter.Value = value?.GetUnderlyingPrimitiveType();
                }

                public override {{name}} Parse(object value)
                {
                    return value switch
                    {
                        int intValue => new {{name}}(intValue),
                        short shortValue => new {{name}}(shortValue),
                        long longValue and < int.MaxValue and > int.MinValue => new {{name}}((int)longValue),
                        decimal decimalValue and < int.MaxValue and > int.MinValue => new {{name}}((int)decimalValue),
                        string stringValue when !string.IsNullOrEmpty(stringValue) && int.TryParse(stringValue, out var result) => new {{name}}(result),
                        _ => throw new global::System.InvalidCastException($"Unable to cast object of type {value.GetType()} to {{name}}"),
                    };
                }
            }
            """;
    }

    public static string CreateLongDapperHandler(string name, string dapperTypeHandler)
    {
        return $$"""
            public sealed class {{dapperTypeHandler}} : global::Dapper.SqlMapper.TypeHandler<{{name}}>
            {
                public override void SetValue(global::System.Data.IDbDataParameter parameter, {{name}} value)
                {
                    parameter.Value = value?.GetUnderlyingPrimitiveType();
                }

                public override {{name}} Parse(object value)
                {
                    return value switch
                    {
                        long longValue => new {{name}}(longValue),
                        int intValue => new {{name}}(intValue),
                        short shortValue => new {{name}}(shortValue),
                        decimal decimalValue and < long.MaxValue and > long.MinValue => new {{name}}((long)decimalValue),
                        string stringValue when !string.IsNullOrEmpty(stringValue) && long.TryParse(stringValue, out var result) => new {{name}}(result),
                        _ => throw new global::System.InvalidCastException($"Unable to cast object of type {value.GetType()} to {{name}}"),
                    };
                }
            }
            """;
    }
}

// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;

namespace Tests.Fakes;

/// <summary>
/// A minimal in-memory <see cref="DbCommand"/> used to observe the keepalive/monitoring queries the
/// <c>ConnectionMonitor</c> issues. Execution is delegated to a configurable callback on the owning
/// <see cref="FakeDbConnection"/> so a test can count invocations or simulate a stalled (hung) connection that only
/// returns when the command timeout would fire.
/// </summary>
internal sealed class FakeDbCommand(FakeDbConnection connection) : DbCommand
{
    private readonly FakeParameterCollection _parameters = new();

    [AllowNull]
    public override string CommandText { get; set; } = string.Empty;

    public override int CommandTimeout { get; set; }

    public override CommandType CommandType { get; set; } = CommandType.Text;

    public override bool DesignTimeVisible { get; set; }

    public override UpdateRowSource UpdatedRowSource { get; set; }

    protected override DbConnection? DbConnection { get; set; } = connection;

    protected override DbParameterCollection DbParameterCollection => _parameters;

    protected override DbTransaction? DbTransaction { get; set; }

    public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        return await connection.OnExecuteNonQueryAsync(this, cancellationToken).ConfigureAwait(false);
    }

    public override int ExecuteNonQuery()
    {
        throw new NotSupportedException("Async-only fake.");
    }

    public override object ExecuteScalar()
    {
        throw new NotSupportedException("Async-only fake.");
    }

    public override void Prepare() { }

    public override void Cancel() { }

    protected override DbParameter CreateDbParameter()
    {
        return new FakeDbParameter();
    }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        throw new NotSupportedException("Async-only fake.");
    }

    private sealed class FakeParameterCollection : DbParameterCollection
    {
        private readonly List<object> _items = [];

        public override int Count => _items.Count;

        public override object SyncRoot { get; } = new();

        public override int Add(object value)
        {
            _items.Add(value);

            return _items.Count - 1;
        }

        public override void AddRange(Array values)
        {
            foreach (var value in values)
            {
                _items.Add(value);
            }
        }

        public override void Clear()
        {
            _items.Clear();
        }

        public override bool Contains(object value)
        {
            return _items.Contains(value);
        }

        public override bool Contains(string value)
        {
            return false;
        }

        public override void CopyTo(Array array, int index)
        {
            ((System.Collections.ICollection)_items).CopyTo(array, index);
        }

        public override System.Collections.IEnumerator GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        public override int IndexOf(object value)
        {
            return _items.IndexOf(value);
        }

        public override int IndexOf(string parameterName)
        {
            return -1;
        }

        public override void Insert(int index, object value)
        {
            _items.Insert(index, value);
        }

        public override void Remove(object value)
        {
            _items.Remove(value);
        }

        public override void RemoveAt(int index)
        {
            _items.RemoveAt(index);
        }

        public override void RemoveAt(string parameterName) { }

        protected override DbParameter GetParameter(int index)
        {
            return (DbParameter)_items[index];
        }

        protected override DbParameter GetParameter(string parameterName)
        {
            throw new NotSupportedException();
        }

        protected override void SetParameter(int index, DbParameter value)
        {
            _items[index] = value;
        }

        protected override void SetParameter(string parameterName, DbParameter value)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }

        public override ParameterDirection Direction { get; set; }

        public override bool IsNullable { get; set; }

        [AllowNull]
        public override string ParameterName { get; set; } = string.Empty;

        public override int Size { get; set; }

        [AllowNull]
        public override string SourceColumn { get; set; } = string.Empty;

        public override bool SourceColumnNullMapping { get; set; }

        public override object? Value { get; set; }

        public override void ResetDbType() { }
    }
}

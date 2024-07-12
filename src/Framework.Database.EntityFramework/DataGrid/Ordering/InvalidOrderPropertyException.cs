namespace Framework.Database.EntityFramework.DataGrid.Ordering;

public sealed class InvalidOrderPropertyException : Exception
{
    public InvalidOrderPropertyException(string message, Exception? innerException)
        : base(message, innerException) { }
}

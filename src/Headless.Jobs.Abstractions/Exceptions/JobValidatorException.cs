namespace Headless.Jobs.Exceptions;

public class JobValidatorException : Exception
{
    public JobValidatorException(string message)
        : base(message) { }
}

namespace Aletheia.Foundation.Exceptions;

public class SecurityException : DomainException
{
    public SecurityException(string message)
        : base(message)
    {
    }

    public SecurityException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

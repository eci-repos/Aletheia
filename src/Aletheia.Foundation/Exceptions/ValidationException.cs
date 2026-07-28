using Aletheia.Foundation.Validation;

namespace Aletheia.Foundation.Exceptions;

public class ValidationException : DomainException
{
    public ValidationException(string message)
        : base(message)
    {
        Errors = Array.Empty<string>();
    }

    public ValidationException(string message, IEnumerable<string> errors)
        : base(message)
    {
        Errors = errors?.ToArray() ?? Array.Empty<string>();
    }

    public ValidationException(string message, ValidationResult validationResult)
        : base(message)
    {
        Errors = validationResult?.Errors.ToArray() ?? Array.Empty<string>();
    }

    public IReadOnlyCollection<string> Errors { get; }
}

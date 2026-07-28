namespace Aletheia.Foundation.Validation;

public sealed class ValidationResult
{
    private readonly List<string> _errors = new();

    public ValidationResult()
    {
    }

    public ValidationResult(IEnumerable<string> errors)
    {
        if (errors is null)
        {
            throw new ArgumentNullException(nameof(errors));
        }

        _errors.AddRange(errors.Where(error => !string.IsNullOrWhiteSpace(error)));
    }

    public IReadOnlyCollection<string> Errors => _errors.AsReadOnly();

    public bool IsValid => _errors.Count == 0;

    public void AddError(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            throw new ArgumentException("Validation error is required.", nameof(error));
        }

        _errors.Add(error);
    }
}

namespace RealLifeServer.Application.Common.Exceptions;

public class ValidationException : Exception
{
    public ValidationException(string message) : base(message)
    {
    }

    public ValidationException(IEnumerable<string> errors)
        : base(string.Join(" ", errors))
    {
        Errors = errors.ToArray();
    }

    public IReadOnlyCollection<string> Errors { get; } = Array.Empty<string>();
}

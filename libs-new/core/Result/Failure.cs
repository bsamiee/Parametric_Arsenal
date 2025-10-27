using System;
using System.Collections.Generic;

namespace Arsenal.Core.Result;

/// <summary>Represents a failure with a diagnostic code, message, and optional metadata.</summary>
public sealed record Failure
{
    /// <summary>Initializes a new failure with the specified code, message, and optional metadata.</summary>
    /// <param name="code">The failure code.</param>
    /// <param name="message">The failure message.</param>
    /// <param name="metadata">Optional metadata associated with the failure.</param>
    public Failure(string code, string message, IReadOnlyDictionary<string, object?>? metadata = null)
    {
        Code = string.IsNullOrWhiteSpace(code)
            ? throw new ArgumentException("Failure code cannot be null or whitespace.", nameof(code))
            : code;

        Message = string.IsNullOrWhiteSpace(message)
            ? throw new ArgumentException("Failure message cannot be null or whitespace.", nameof(message))
            : message;

        Metadata = metadata;
    }

    /// <summary>Gets the failure code.</summary>
    public string Code { get; }

    /// <summary>Gets the failure message.</summary>
    public string Message { get; }

    /// <summary>Gets the optional metadata associated with the failure.</summary>
    public IReadOnlyDictionary<string, object?>? Metadata { get; }

    /// <summary>Creates a failure from an exception.</summary>
    /// <param name="exception">The exception to convert.</param>
    /// <param name="code">The failure code to use.</param>
    /// <returns>A failure containing the exception details.</returns>
    public static Failure From(Exception exception, string code = "exception")
    {
        ArgumentNullException.ThrowIfNull(exception);

        IReadOnlyDictionary<string, object?> metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["exceptionType"] = exception.GetType().FullName,
            ["stackTrace"] = exception.StackTrace,
            ["source"] = exception.Source
        };

        return new Failure(code, exception.Message, metadata);
    }
}

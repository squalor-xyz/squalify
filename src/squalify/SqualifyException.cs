// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// The single public failure type: every error squalify reports surfaces as this exception. A failure that a caller may need to
/// tell apart from the others also carries a stable <see cref="Code"/>; see <see cref="SqualifyErrorCodes"/>.
/// </summary>
public class SqualifyException : Exception
{
    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">What went wrong, in plain language.</param>
    public SqualifyException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and the lower-level cause.</summary>
    /// <param name="message">What went wrong, in plain language.</param>
    /// <param name="innerException">The wrapped lower-level exception.</param>
    public SqualifyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception with a message and a code.</summary>
    /// <param name="message">What went wrong, in plain language.</param>
    /// <param name="code">The stable code of the failure; a value of <see cref="SqualifyErrorCodes"/>.</param>
    public SqualifyException(string message, string code)
        : base(message)
    {
        Code = code;
    }

    /// <summary>Creates an exception with a message, a code and the lower-level cause.</summary>
    /// <param name="message">What went wrong, in plain language.</param>
    /// <param name="code">The stable code of the failure; a value of <see cref="SqualifyErrorCodes"/>.</param>
    /// <param name="innerException">The wrapped lower-level exception.</param>
    public SqualifyException(string message, string code, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    /// <summary>
    /// The stable code of the failure, one of the values of <see cref="SqualifyErrorCodes"/>, or null when the failure has no code
    /// (the message is then the only description). A consumer that needs to recognise a failure compares this and never
    /// the message, which is for people and may change. Only the failures listed in <see cref="SqualifyErrorCodes"/> have a code.
    /// </summary>
    public string? Code { get; }
}

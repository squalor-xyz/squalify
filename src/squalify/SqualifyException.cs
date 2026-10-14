// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>The single public failure type: every error squalify reports surfaces as this exception.</summary>
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
}

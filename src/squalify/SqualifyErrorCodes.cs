// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// The values of <see cref="SqualifyException.Code"/>. The values are stable public API: a value never changes its meaning
/// and is never reused for another failure. New codes may be added in a minor release, so a consumer that switches on the
/// code keeps a default branch. A failure with no code here has a null <see cref="SqualifyException.Code"/>.
/// </summary>
public static class SqualifyErrorCodes
{
    /// <summary>
    /// The first record of a text file, its line terminator included, is longer than <see cref="ProfileOptions.MaxFirstRecordBytes"/>
    /// bytes: nothing was profiled and no more than the ceiling (plus the one code unit that tells a CR from a CRLF) was read.
    /// </summary>
    public const string FirstRecordTooLong = "firstRecordTooLong";
}

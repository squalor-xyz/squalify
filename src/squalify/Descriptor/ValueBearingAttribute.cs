// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// Marks a descriptor member that can carry literal data values. Every marked member must be listed in
/// <see cref="ValuesPolicy"/>, and a test checks the two against each other by reflection, so a member added to the
/// model cannot leak values unnoticed. A member that is value-bearing only in some cases (the statistics of a constant
/// field, the regex of a comment line) is marked too; the policy entry says when.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
internal sealed class ValueBearingAttribute : Attribute
{
}

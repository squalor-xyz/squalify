// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// The interim <c>kind</c> labels and rule ids, named once. Until the rule table arrives, the label follows the class of the byte pass.
/// The rule ids are those of the proposed table (K09 text, K11 binary, K12 empty) and are permanent. The schema enums
/// <c>kind.label</c> and <c>kind.ruleId</c> list <see cref="Labels"/> and <see cref="RuleIds"/>.
/// </summary>
internal static class InterimKinds
{
    /// <summary>The label of a file that is empty.</summary>
    public static readonly string EmptyLabel = "empty";

    /// <summary>The label of a file that is binary.</summary>
    public static readonly string BinaryLabel = "binary";

    /// <summary>The label of a file that is text.</summary>
    public static readonly string TextLabel = "text";

    /// <summary>The rule id of an empty file.</summary>
    public static readonly string EmptyRuleId = "K12";

    /// <summary>The rule id of a binary file.</summary>
    public static readonly string BinaryRuleId = "K11";

    /// <summary>The rule id of a text file.</summary>
    public static readonly string TextRuleId = "K09";

    /// <summary>Every label this version writes, in ordinal order.</summary>
    public static readonly IReadOnlyList<string> Labels = [BinaryLabel, EmptyLabel, TextLabel];

    /// <summary>Every rule id this version writes, in ordinal order.</summary>
    public static readonly IReadOnlyList<string> RuleIds = [TextRuleId, BinaryRuleId, EmptyRuleId];

    /// <summary>The kind of a file from the class of the byte pass.</summary>
    /// <param name="encodingKind">The class: <c>empty</c>, <c>binary</c> or <c>text</c>.</param>
    /// <returns>The kind label.</returns>
    public static KindLabel For(string encodingKind)
    {
        return encodingKind switch
        {
            "empty" => new KindLabel(EmptyLabel, EmptyRuleId, 1.0),
            "binary" => new KindLabel(BinaryLabel, BinaryRuleId, 1.0),
            _ => new KindLabel(TextLabel, TextRuleId, 1.0),
        };
    }
}

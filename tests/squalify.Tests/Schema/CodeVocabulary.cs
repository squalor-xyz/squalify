// SPDX-License-Identifier: Apache-2.0
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace squalor.squalify.Tests.Schema;

/// <summary>
/// The closed sets of the descriptor as the code defines or can produce them, read from the compiled library: the issue codes, the
/// evidence stages, the format families, and the names of layout regions. The schema's enums must equal them.
/// <para>
/// Issue codes have no list in the code: each is a string literal passed to <c>new Issue(</c> or to the helper that builds a located
/// issue. The scan reads the IL of every method of the library and takes, for each such call, the string literal loaded just before
/// it (the code is the first argument, and the arguments after it hold no string literal). A code built any other way, from a variable,
/// would not be found; the fixtures' goldens validated against the schema (<c>AllGoldens_ValidateAgainstSchema</c>) catch such a code
/// when a fixture produces it.
/// </para>
/// </summary>
internal static class CodeVocabulary
{
    private const int MaxInstructionsBetweenLiteralAndCall = 48;

    private static readonly Lazy<IReadOnlyList<string>> Codes = new(ScanIssueCodes);
    private static readonly Lazy<(IReadOnlyList<string> Kinds, IReadOnlyList<string> Names)> Regions = new(ScanRegions);

    /// <summary>The issue codes the library can write, ordinally sorted.</summary>
    public static IReadOnlyList<string> IssueCodes() => Codes.Value;

    /// <summary>The evidence stages, from the stage order of the profiler, ordinally sorted.</summary>
    public static IReadOnlyList<string> EvidenceStages()
    {
        var field = typeof(Profiler).GetField("StageOrder", BindingFlags.NonPublic | BindingFlags.Static) ?? throw new InvalidOperationException("Profiler.StageOrder was not found.");
        return ((string[])field.GetValue(null)!).Order(StringComparer.Ordinal).ToArray();
    }

    /// <summary>The families this version writes: those of the registered format profilers, and <c>empty</c> for a zero-length file.</summary>
    public static IReadOnlyList<string> Families()
    {
        var field = typeof(FormatRegistry).GetField("_profilers", BindingFlags.NonPublic | BindingFlags.Instance) ?? throw new InvalidOperationException("FormatRegistry._profilers was not found.");
        var profilers = (IReadOnlyList<IFormatProfiler>)field.GetValue(FormatRegistry.Default)!;
        return profilers.Select(static p => p.Family).Append("empty").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    /// <summary>The kinds of layout region that profiling the fixtures produces (<c>block</c> included), ordinally sorted.</summary>
    public static IReadOnlyList<string> RegionKinds() => Regions.Value.Kinds;

    /// <summary>
    /// The names that <c>issues[].regions</c> can take: what the locator gives for the start of every region of every fixture's layout,
    /// and for an offset that no region holds. Ordinally sorted.
    /// </summary>
    public static IReadOnlyList<string> RegionNames() => Regions.Value.Names;

    private static (IReadOnlyList<string>, IReadOnlyList<string>) ScanRegions()
    {
        var kinds = new SortedSet<string>(StringComparer.Ordinal);
        var names = new SortedSet<string>(StringComparer.Ordinal);
        var root = Path.Combine(AppContext.BaseDirectory, "fixtures");
        foreach (var source in Directory.GetFiles(root, "*", SearchOption.AllDirectories).Where(static f => !f.EndsWith(".squalify.json", StringComparison.Ordinal) && File.Exists(f + ".squalify.json")))
        {
            var layout = Profiler.Profile(source, ProfileOptions.Create(false, [])).Layout;
            Collect(layout, layout.Regions, kinds, names);
            names.UnionWith(InvalidByteLocator.RegionNames(layout, [long.MaxValue]));
        }

        return (kinds.ToArray(), names.ToArray());
    }

    private static void Collect(LayoutSection layout, IReadOnlyList<Region> regions, SortedSet<string> kinds, SortedSet<string> names)
    {
        foreach (var region in regions)
        {
            kinds.Add(region.Kind);
            names.UnionWith(InvalidByteLocator.RegionNames(layout, [region.StartByte]));
            Collect(layout, region.Regions ?? [], kinds, names);
        }
    }

    private static IReadOnlyList<string> ScanIssueCodes()
    {
        var opCodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(static f => (OpCode)f.GetValue(null)!)
            .ToDictionary(static o => (ushort)o.Value);
        var codes = new SortedSet<string>(StringComparer.Ordinal);
        using var stream = File.OpenRead(typeof(Profiler).Assembly.Location);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        foreach (var handle in metadata.MethodDefinitions)
        {
            var definition = metadata.GetMethodDefinition(handle);
            if (definition.RelativeVirtualAddress == 0)
            {
                continue;
            }

            var il = pe.GetMethodBody(definition.RelativeVirtualAddress).GetILBytes()!;
            string? literal = null;
            var sinceLiteral = 0;
            for (var at = 0; at < il.Length;)
            {
                var code = il[at] == 0xFE ? (ushort)(0xFE00 | il[at + 1]) : il[at];
                at += il[at] == 0xFE ? 2 : 1;
                var op = opCodes[code];
                var operand = op.OperandType == OperandType.InlineNone ? 0 : BitConverter.ToInt32(il, Math.Min(at, il.Length - 4));
                sinceLiteral++;
                if (code == OpCodes.Ldstr.Value)
                {
                    literal = metadata.GetUserString(MetadataTokens.UserStringHandle(operand & 0xFFFFFF));
                    sinceLiteral = 0;
                }
                else if (literal is not null && sinceLiteral <= MaxInstructionsBetweenLiteralAndCall && (code == OpCodes.Newobj.Value || code == OpCodes.Call.Value) && BuildsAnIssue(metadata, operand))
                {
                    codes.Add(literal);
                    literal = null;
                }

                at += op.OperandType switch
                {
                    OperandType.InlineNone => 0,
                    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                    OperandType.InlineVar => 2,
                    OperandType.InlineI8 or OperandType.InlineR => 8,
                    OperandType.InlineSwitch => 4 + (4 * operand),
                    _ => 4,
                };
            }
        }

        return codes.ToArray();
    }

    // The constructor of Issue, or the method that builds a located issue from a code.
    private static bool BuildsAnIssue(MetadataReader metadata, int token)
    {
        var handle = MetadataTokens.EntityHandle(token);
        switch (handle.Kind)
        {
            case HandleKind.MethodDefinition:
                var method = metadata.GetMethodDefinition((MethodDefinitionHandle)handle);
                var owner = metadata.GetTypeDefinition(method.GetDeclaringType());
                var methodName = metadata.GetString(method.Name);
                return (methodName == ".ctor" && metadata.GetString(owner.Name) == "Issue") || methodName == "LocatedIssue";
            case HandleKind.MemberReference:
                var member = metadata.GetMemberReference((MemberReferenceHandle)handle);
                return metadata.GetString(member.Name) == ".ctor" && member.Parent.Kind == HandleKind.TypeReference && metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)member.Parent).Name) == "Issue";
            default:
                return false;
        }
    }
}

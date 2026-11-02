// SPDX-License-Identifier: Apache-2.0
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace squalor.squalify.Tests.Schema;

/// <summary>
/// The closed sets of the descriptor as the code produces them, read from the compiled library: the format families, the names of
/// layout regions, and (as a check on the registries) what the IL says about the issue codes and the evidence stages.
/// <para>
/// The issue codes and the evidence stages have registries (<c>IssueCodes.All</c>, <c>EvidenceStages.Order</c>), and the schema's enums
/// must equal them. The registries hold their values in <c>static readonly</c> fields, not constants, because a constant is inlined as a
/// string literal at every use and the scan could not tell a use of the registry from a raw literal. The scan reads the IL of every
/// method of the library; what it finds is a literal at a call site that bypasses the registry. For an issue code, the string literal loaded
/// within 48 instructions before <c>new Issue(</c> or <c>LocatedIssue</c> (the code is the first argument, and the arguments after it hold
/// no string literal). For a stage, a string literal followed by the topic literal before <c>new Evidence(</c>. The scan is a subset
/// check and a bypass check only: a code or a stage built any other way is caught by the goldens validated against the schema
/// (<c>AllGoldens_ValidateAgainstSchema</c>) when a fixture produces it, and an unknown stage is rejected when the profiler sorts the evidence.
/// </para>
/// </summary>
internal static class CodeVocabulary
{
    private const int MaxInstructionsBetweenLiteralAndCall = 48;

    private static readonly Lazy<IReadOnlyList<string>> IssueScan = new(ScanIssueCodes);
    private static readonly Lazy<(IReadOnlyList<string> Stages, IReadOnlyList<string> Literals)> EvidenceScan = new(ScanEvidenceSites);
    private static readonly Lazy<IReadOnlyList<string>> KindScan = new(static () => ScanLiteralsBeforeConstructor("KindLabel", "InterimKinds"));
    private static readonly Lazy<(IReadOnlyList<string> Kinds, IReadOnlyList<string> Names)> Regions = new(ScanRegions);

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

    /// <summary>
    /// What the IL scan finds: the issue codes passed as a string literal to <c>new Issue(</c> or to <c>LocatedIssue</c>, ordinally sorted,
    /// outside the registry. The registry holds its codes in <c>static readonly</c> fields, not constants, so a use of the registry is
    /// a field load in the IL and never a literal; every code the scan finds is a literal that bypasses it.
    /// </summary>
    public static IReadOnlyList<string> IlIssueCodes() => IssueScan.Value;

    /// <summary>
    /// The stage that a literal puts in <c>new Evidence(</c>, as the IL scan reads it: a string literal loaded right before another string
    /// literal (the topic) that no field load precedes, within the instructions before the constructor call. Ordinally sorted. A stage that
    /// comes from the registry is a field load, so it is not found; every stage found is a literal that bypasses the registry.
    /// </summary>
    public static IReadOnlyList<string> IlEvidenceStageLiterals() => EvidenceScan.Value.Stages;

    /// <summary>Every string literal in the instructions before a call of the constructor of <c>Evidence</c>, ordinally sorted.</summary>
    public static IReadOnlyList<string> IlLiteralsAtEvidenceSites() => EvidenceScan.Value.Literals;

    /// <summary>
    /// Every string literal in the instructions before a call of the constructor of <c>KindLabel</c>, outside <c>InterimKinds</c>, ordinally
    /// sorted. The registry holds the labels and rule ids in <c>static readonly</c> fields, so every literal found bypasses it.
    /// </summary>
    public static IReadOnlyList<string> IlLiteralsAtKindLabelSites() => KindScan.Value;

    private readonly record struct Instruction(ushort Code, string? Literal, string? TargetType, string? TargetName);

    private static List<(string Owner, Instruction[] Body)> ReadMethodBodies()
    {
        var opCodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(static f => (OpCode)f.GetValue(null)!)
            .ToDictionary(static o => (ushort)o.Value);
        var bodies = new List<(string, Instruction[])>();
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
            var instructions = new List<Instruction>();
            for (var at = 0; at < il.Length;)
            {
                var code = il[at] == 0xFE ? (ushort)(0xFE00 | il[at + 1]) : il[at];
                at += il[at] == 0xFE ? 2 : 1;
                var op = opCodes[code];
                var operand = op.OperandType == OperandType.InlineNone ? 0 : BitConverter.ToInt32(il, Math.Min(at, il.Length - 4));
                if (code == OpCodes.Ldstr.Value)
                {
                    instructions.Add(new Instruction(code, metadata.GetUserString(MetadataTokens.UserStringHandle(operand & 0xFFFFFF)), null, null));
                }
                else if (code == OpCodes.Newobj.Value || code == OpCodes.Call.Value || code == OpCodes.Callvirt.Value || code == OpCodes.Ldsfld.Value)
                {
                    var (type, name) = Target(metadata, operand);
                    instructions.Add(new Instruction(code, null, type, name));
                }
                else
                {
                    instructions.Add(new Instruction(code, null, null, null));
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

            bodies.Add((metadata.GetString(metadata.GetTypeDefinition(definition.GetDeclaringType()).Name), instructions.ToArray()));
        }

        return bodies;
    }

    // The type name and the member name that a method, member or field token names, or nulls for any other kind of token.
    private static (string? Type, string? Name) Target(MetadataReader metadata, int token)
    {
        var handle = MetadataTokens.EntityHandle(token);
        switch (handle.Kind)
        {
            case HandleKind.MethodDefinition:
                var method = metadata.GetMethodDefinition((MethodDefinitionHandle)handle);
                return (metadata.GetString(metadata.GetTypeDefinition(method.GetDeclaringType()).Name), metadata.GetString(method.Name));
            case HandleKind.FieldDefinition:
                var field = metadata.GetFieldDefinition((FieldDefinitionHandle)handle);
                return (metadata.GetString(metadata.GetTypeDefinition(field.GetDeclaringType()).Name), metadata.GetString(field.Name));
            case HandleKind.MemberReference:
                var member = metadata.GetMemberReference((MemberReferenceHandle)handle);
                var type = member.Parent.Kind == HandleKind.TypeReference ? metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)member.Parent).Name) : null;
                return (type, metadata.GetString(member.Name));
            default:
                return (null, null);
        }
    }

    private static IReadOnlyList<string> ScanIssueCodes()
    {
        var codes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (_, body) in ReadMethodBodies().Where(static b => b.Owner != "IssueCodes"))
        {
            string? literal = null;
            var sinceLiteral = 0;
            for (var i = 0; i < body.Length; i++)
            {
                var instruction = body[i];
                sinceLiteral++;
                if (instruction.Literal is not null)
                {
                    // A literal that a string comparison consumes (charset is "utf-8") is not an argument.
                    var consumed = i + 1 < body.Length && body[i + 1].TargetType == "String";
                    literal = consumed ? null : instruction.Literal;
                    sinceLiteral = 0;
                }
                else if (literal is not null && sinceLiteral <= MaxInstructionsBetweenLiteralAndCall && IsCall(instruction) && BuildsAnIssue(instruction))
                {
                    codes.Add(literal);
                    literal = null;
                }
            }
        }

        return codes.ToArray();
    }

    private static IReadOnlyList<string> ScanLiteralsBeforeConstructor(string type, string registry)
    {
        var literals = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (_, body) in ReadMethodBodies().Where(b => b.Owner != registry))
        {
            for (var i = 0; i < body.Length; i++)
            {
                if (body[i].Code == OpCodes.Newobj.Value && body[i].TargetType == type && body[i].TargetName == ".ctor")
                {
                    literals.UnionWith(body[Math.Max(0, i - MaxInstructionsBetweenLiteralAndCall)..i].Where(static x => x.Literal is not null).Select(static x => x.Literal!));
                }
            }
        }

        return literals.ToArray();
    }

    private static (IReadOnlyList<string> Stages, IReadOnlyList<string> Literals) ScanEvidenceSites()
    {
        var stages = new SortedSet<string>(StringComparer.Ordinal);
        var literals = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (_, body) in ReadMethodBodies())
        {
            for (var i = 0; i < body.Length; i++)
            {
                if (body[i].Code != OpCodes.Newobj.Value || body[i].TargetType != "Evidence" || body[i].TargetName != ".ctor")
                {
                    continue;
                }

                for (var back = Math.Max(0, i - MaxInstructionsBetweenLiteralAndCall); back < i; back++)
                {
                    if (body[back].Literal is not { } literal)
                    {
                        continue;
                    }

                    literals.Add(literal);
                    var topicFollows = body[back + 1].Literal is not null;
                    var afterFieldLoad = back > 0 && body[back - 1].Code == OpCodes.Ldsfld.Value;
                    if (topicFollows && !afterFieldLoad)
                    {
                        stages.Add(literal);
                    }
                }
            }
        }

        return (stages.ToArray(), literals.ToArray());
    }

    private static bool IsCall(Instruction instruction) => instruction.Code == OpCodes.Newobj.Value || instruction.Code == OpCodes.Call.Value;

    // The constructor of Issue, or the method that builds a located issue from a code.
    private static bool BuildsAnIssue(Instruction instruction)
    {
        return (instruction.TargetName == ".ctor" && instruction.TargetType == "Issue") || instruction.TargetName == "LocatedIssue";
    }
}

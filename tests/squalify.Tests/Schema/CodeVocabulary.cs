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
/// method of the library; what it finds is a literal at a call site that bypasses the registry. For an issue code, the string literal that
/// is the first argument of <c>new Issue(</c> or <c>LocatedIssue</c>, found by following the stack from the literal to the call. A literal
/// in a later argument, or one that an instruction pops before the call (a call, a comparison, a dup, a pop), is not a code. A store to a
/// local or an argument leaves the stack unknown, so a literal that reaches the code through a local is still a code. For a stage, a string
/// literal followed by the topic literal before <c>new Evidence(</c>. The scan is a subset check and a bypass check only: a code or a
/// stage built any other way is caught by the goldens validated against the schema (<c>AllGoldens_ValidateAgainstSchema</c>) when a fixture
/// produces it, and an unknown stage is rejected when the profiler sorts the evidence.
/// </para>
/// </summary>
internal static class CodeVocabulary
{
    private const int MaxInstructionsBetweenLiteralAndCall = 48;

    private static readonly Lazy<IReadOnlyList<string>> IssueScan = new(() => ScanIssueCodes(null, null));
    private static readonly Lazy<(IReadOnlyList<string> Stages, IReadOnlyList<string> Literals)> EvidenceScan = new(ScanEvidenceSites);
    private static readonly Lazy<IReadOnlyList<string>> KindScan = new(static () => ScanLiteralsBeforeConstructor("KindLabel", "InterimKinds"));
    private static readonly Lazy<(IReadOnlyList<string> Kinds, IReadOnlyList<string> Names)> Regions = new(ScanRegions);

    // The stores that take a value off the stack into a local or an argument, in their short and long forms, by opcode value.
    private static readonly HashSet<ushort> SlotStores = new(
        new[] { OpCodes.Stloc_0, OpCodes.Stloc_1, OpCodes.Stloc_2, OpCodes.Stloc_3, OpCodes.Stloc_S, OpCodes.Stloc, OpCodes.Starg_S, OpCodes.Starg }
            .Select(static op => (ushort)op.Value));

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
    /// What the IL scan finds: the issue codes passed as a string literal in the first argument of <c>new Issue(</c> or of
    /// <c>LocatedIssue</c>, ordinally sorted, outside the registry. The registry holds its codes in <c>static readonly</c> fields, not
    /// constants, so a use of the registry is a field load in the IL and never a literal; every code the scan finds is a literal that
    /// bypasses it.
    /// </summary>
    /// <param name="assembly">The assembly to read; the library by default. Only the test of the scan itself reads another.</param>
    /// <param name="onlyType">Read only this top-level type and the types nested in it; every type by default.</param>
    public static IReadOnlyList<string> IlIssueCodes(Assembly? assembly = null, string? onlyType = null) =>
        assembly is null && onlyType is null ? IssueScan.Value : ScanIssueCodes(assembly, onlyType);

    /// <summary>
    /// The places where an <c>Issue</c> is made without the code coming from the registry, as <c>Type.Method</c> (ordinally sorted). The check
    /// is positive: every <c>newobj Issue::.ctor</c> and every call of <c>LocatedIssue</c> must have a load of an <c>IssueCodes</c> field
    /// (<c>ldsfld</c>) since the previous such site in the same method. A literal, a variable and a field of another
    /// class fail it, and so does a concatenation of those. It does not prove that the loaded field is the code: an expression that also loads a
    /// registry field (<c>IssueCodes.X + suffix</c>) passes. The check proves itself on a probe (<c>IssueSiteProbe</c>). Two sites are exempt by name, because they pass a code on and do not choose one:
    /// the copy constructor that the compiler makes for the record <c>Issue</c> (<c>Issue.&lt;Clone&gt;$</c>, behind a <c>with</c>), and
    /// <c>Profiler.LocatedIssue</c>, the forwarder whose callers pass the registry field in.
    /// </summary>
    /// <param name="exempt">The sites to skip, as <c>Type.Method</c>; the default is the two named above.</param>
    /// <param name="assembly">The assembly to read; the library by default. Only the test of the scan itself reads another.</param>
    /// <param name="onlyType">Read only this top-level type and the types nested in it (a lambda is in one); every type by default.</param>
    public static IReadOnlyList<string> IlIssueSitesWithoutARegistryCode(IReadOnlyCollection<string>? exempt = null, Assembly? assembly = null, string? onlyType = null)
    {
        exempt ??= ExemptIssueSites;
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (owner, method, body) in ReadMethodBodies(assembly, onlyType).Where(static b => b.Owner != "IssueCodes"))
        {
            var loadedARegistryField = false;
            foreach (var instruction in body)
            {
                if (instruction.Code == OpCodes.Ldsfld.Value && instruction.TargetType == "IssueCodes")
                {
                    loadedARegistryField = true;
                }
                else if (IsCall(instruction) && BuildsAnIssue(instruction))
                {
                    if (!loadedARegistryField && !exempt.Contains(owner + "." + method))
                    {
                        found.Add(owner + "." + method);
                    }

                    loadedARegistryField = false;
                }
            }
        }

        return found.ToArray();
    }

    /// <summary>The two sites that make an <c>Issue</c> without choosing its code; see <see cref="IlIssueSitesWithoutARegistryCode"/>.</summary>
    public static readonly IReadOnlyList<string> ExemptIssueSites = ["Issue.<Clone>$", "Profiler.LocatedIssue"];

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

    // Pops and Pushes are the values the instruction takes from and leaves on the stack, -1 when the reader cannot give them.
    private readonly record struct Instruction(ushort Code, string? Literal, string? TargetType, string? TargetName, FlowControl Flow, int Pops, int Pushes);

    private static List<(string Owner, string Method, Instruction[] Body)> ReadMethodBodies(Assembly? assembly = null, string? onlyType = null)
    {
        var opCodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(static f => (OpCode)f.GetValue(null)!)
            .ToDictionary(static o => (ushort)o.Value);
        var bodies = new List<(string, string, Instruction[])>();
        using var stream = File.OpenRead((assembly ?? typeof(Profiler).Assembly).Location);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        foreach (var handle in metadata.MethodDefinitions)
        {
            var definition = metadata.GetMethodDefinition(handle);
            if (definition.RelativeVirtualAddress == 0)
            {
                continue;
            }

            var owner = TypePath(metadata, definition.GetDeclaringType());
            if (onlyType is not null && owner.Split('+')[0] != onlyType)
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
                var (pops, pushes) = StackEffect(metadata, code, op, operand);
                if (code == OpCodes.Ldstr.Value)
                {
                    instructions.Add(new Instruction(code, metadata.GetUserString(MetadataTokens.UserStringHandle(operand & 0xFFFFFF)), null, null, op.FlowControl, pops, pushes));
                }
                else if (code == OpCodes.Newobj.Value || code == OpCodes.Call.Value || code == OpCodes.Callvirt.Value || code == OpCodes.Ldsfld.Value)
                {
                    var (type, name) = Target(metadata, operand);
                    instructions.Add(new Instruction(code, null, type, name, op.FlowControl, pops, pushes));
                }
                else
                {
                    instructions.Add(new Instruction(code, null, null, null, op.FlowControl, pops, pushes));
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

            bodies.Add((owner, metadata.GetString(definition.Name), instructions.ToArray()));
        }

        return bodies;
    }

    // The name of a type, with the names of the types that enclose it in front, joined by '+' (a lambda lives in IssueSiteProbe+<>c).
    private static string TypePath(MetadataReader metadata, TypeDefinitionHandle handle)
    {
        var definition = metadata.GetTypeDefinition(handle);
        var name = metadata.GetString(definition.Name);
        var outer = definition.GetDeclaringType();
        return outer.IsNil ? name : TypePath(metadata, outer) + "+" + name;
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

    // The values an instruction pops and pushes, -1 for each the reader cannot give (calli, or a call token that names no method). A call pops
    // its parameters, and its this unless it is a newobj, and pushes its result unless it returns void; a newobj always pushes the new object.
    private static (int Pops, int Pushes) StackEffect(MetadataReader metadata, ushort code, OpCode op, int operand)
    {
        if (code == OpCodes.Call.Value || code == OpCodes.Callvirt.Value || code == OpCodes.Newobj.Value)
        {
            if (MethodSignature(metadata, operand) is not { } signature)
            {
                return (-1, -1);
            }

            var newobj = code == OpCodes.Newobj.Value;
            var pops = signature.Parameters + (signature.Instance && !newobj ? 1 : 0);
            var pushes = (newobj || !signature.ReturnsVoid) ? 1 : 0;
            return (pops, pushes);
        }

        return (ValueCount(op.StackBehaviourPop.ToString()), ValueCount(op.StackBehaviourPush.ToString()));
    }

    // The number of values a stack behaviour names, from the BCL's name for it: one segment per value ("Popref_popi_popi" pops three,
    // "Push1_push1" pushes two), with "Pop0" and "Push0" for none. A variable count ("Varpop", "Varpush") is -1.
    private static int ValueCount(string behaviour)
    {
        if (behaviour.StartsWith("Var", StringComparison.Ordinal))
        {
            return -1;
        }

        return behaviour is "Pop0" or "Push0" ? 0 : behaviour.Split('_').Length;
    }

    // The parameter count, whether the method is an instance method and whether it returns void, from the signature of the method that a
    // call token names: a method definition, a member reference, or a generic instantiation of either. Null for any other token.
    private static (int Parameters, bool Instance, bool ReturnsVoid)? MethodSignature(MetadataReader metadata, int token)
    {
        var handle = MetadataTokens.EntityHandle(token);
        if (handle.Kind == HandleKind.MethodSpecification)
        {
            handle = metadata.GetMethodSpecification((MethodSpecificationHandle)handle).Method;
        }

        var blob = handle.Kind switch
        {
            HandleKind.MethodDefinition => metadata.GetMethodDefinition((MethodDefinitionHandle)handle).Signature,
            HandleKind.MemberReference => metadata.GetMemberReference((MemberReferenceHandle)handle).Signature,
            _ => (BlobHandle?)null,
        };
        if (blob is not { } signature)
        {
            return null;
        }

        var reader = metadata.GetBlobReader(signature);
        var header = reader.ReadSignatureHeader();
        if (header.IsGeneric)
        {
            reader.ReadCompressedInteger();
        }

        var parameters = reader.ReadCompressedInteger();
        return (parameters, header.IsInstance, reader.ReadSignatureTypeCode() == SignatureTypeCode.Void);
    }

    private static IReadOnlyList<string> ScanIssueCodes(Assembly? assembly, string? onlyType)
    {
        var codes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (_, _, body) in ReadMethodBodies(assembly, onlyType).Where(static b => b.Owner != "IssueCodes"))
        {
            for (var i = 0; i < body.Length; i++)
            {
                if (body[i].Literal is { } literal && IsCodeArgument(body, i))
                {
                    codes.Add(literal);
                }
            }
        }

        return codes.ToArray();
    }

    // Whether the string literal at index start is the first argument of the issue call that it reaches, which is the code. The values it
    // leaves on the stack are followed in order. An instruction that pops the literal ends the search: it is not an argument. The exception is
    // a store to a local or an argument, which takes the literal into a slot: the literal may be loaded again as the code, so the stack is
    // unknown from there, as after a branch. A call of the Issue constructor or of LocatedIssue decides it: the literal is the first argument
    // when the call pops exactly the literal and the values above it. A return or a throw ends the search. A branch, or an instruction whose
    // effect the reader cannot give, leaves the stack unknown from there (the literal may or may not survive a join), so the literal counts as
    // a code if an issue call follows within the bound: the old reading, on the side of the bypass check that fails loudly.
    private static bool IsCodeArgument(Instruction[] body, int start)
    {
        var depth = 1;
        var uncertain = false;
        for (var i = start + 1; i < body.Length && i <= start + MaxInstructionsBetweenLiteralAndCall; i++)
        {
            var instruction = body[i];
            if (IsCall(instruction) && BuildsAnIssue(instruction))
            {
                return uncertain || instruction.Pops == depth;
            }

            if (instruction.Flow is FlowControl.Return or FlowControl.Throw)
            {
                return false;
            }

            if (uncertain)
            {
                continue;
            }

            if (instruction.Pops < 0 || instruction.Pushes < 0)
            {
                uncertain = true;
                continue;
            }

            if (instruction.Pops >= depth)
            {
                if (SlotStores.Contains(instruction.Code))
                {
                    uncertain = true;
                    continue;
                }

                return false;
            }

            depth += instruction.Pushes - instruction.Pops;
            uncertain = instruction.Flow is FlowControl.Branch or FlowControl.Cond_Branch;
        }

        return false;
    }

    private static IReadOnlyList<string> ScanLiteralsBeforeConstructor(string type, string registry)
    {
        var literals = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (_, _, body) in ReadMethodBodies().Where(b => b.Owner != registry))
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
        foreach (var (_, _, body) in ReadMethodBodies())
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

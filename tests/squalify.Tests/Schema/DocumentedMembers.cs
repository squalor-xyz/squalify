// SPDX-License-Identifier: Apache-2.0
using System.Text.RegularExpressions;

namespace squalor.squalify.Tests.Schema;

/// <summary>An object of the descriptor as the document lists it.</summary>
/// <param name="Path">The path, in the form of <see cref="SchemaSection.Path"/>: names joined by dots, no <c>[]</c>.</param>
/// <param name="Members">The produced members, in the order of the document (a planned member is not one).</param>
internal sealed record DocumentedSection(string Path, IReadOnlyList<string> Members);

/// <summary>What <c>docs/descriptor.md</c> documents: the objects with their members, and what it marks as planned.</summary>
/// <param name="Sections">The produced objects by path (the root is the empty string).</param>
/// <param name="Planned">
/// The planned objects (<c>stdf.records</c>) and members (<c>object/member</c>, as <c>binary/blocks</c>; a planned member of the root
/// is its name alone): documented, and not produced by this version.
/// </param>
internal sealed record DocumentedSet(IReadOnlyDictionary<string, DocumentedSection> Sections, IReadOnlyList<string> Planned);

/// <summary>
/// Reads the member lists out of <c>docs/descriptor.md</c>: the table under "Top level" (the members of the root) and the table
/// under "Key order of nested objects" (one row per object). The grammar of a row of the second table:
/// <list type="bullet">
/// <item>The first cell is the object, in code font (<c>fields[].numeric</c>, relative to the root, with a leading <c>fields</c> read as
/// <c>table.fields</c>). The text after it may say <c>(planned)</c> (the object is not produced) or <c>of a layout-only profile</c> (the
/// row is the layout-only shape of the object, <c>#layoutOnlyField</c>).</item>
/// <item>The second cell is a comma-separated list of members in code font, in order; <c>then</c> may precede one. Text after the first
/// semicolon outside parentheses is prose and is not read. A member may be followed by a parenthesis: a list of names in code font
/// is the members of the nested object of that name; any other text is an annotation, and one that says <c>planned</c> marks the
/// member as planned.</item>
/// </list>
/// A row that cannot be read, or an object listed twice, is an error: the tests must not skip what they cannot understand.
/// </summary>
internal static partial class DocumentedMembers
{
    private const string TopLevelHeading = "## Top level";
    private const string KeyOrderHeading = "### Key order of nested objects";

    /// <summary>Reads the member lists of the document.</summary>
    /// <param name="markdown">The text of the document.</param>
    /// <returns>The documented objects and the planned members.</returns>
    /// <exception cref="InvalidDataException">A table is missing, a row cannot be read or an object is documented twice.</exception>
    public static DocumentedSet Parse(string markdown)
    {
        var sections = new Dictionary<string, DocumentedSection>(StringComparer.Ordinal);
        var planned = new List<string>();

        var top = Rows(markdown, TopLevelHeading);
        if (top is not null)
        {
            var members = new List<string>();
            foreach (var cells in top)
            {
                var name = CodeName(cells[0]) ?? throw new InvalidDataException($"The top-level table has a row whose first cell is not a name in code font: {cells[0]}");
                // Only the third column ("Always") says whether the member is produced; the prose of the description may use the word.
                if (cells.Length > 2 && cells[2].Contains("planned", StringComparison.OrdinalIgnoreCase))
                {
                    planned.Add(name);
                }
                else
                {
                    members.Add(name);
                }
            }

            sections[string.Empty] = new DocumentedSection(string.Empty, members);
        }

        var nested = Rows(markdown, KeyOrderHeading);
        if (nested is not null)
        {
            foreach (var cells in nested)
            {
                ReadRow(cells, sections, planned);
            }
        }

        if (top is null && nested is null)
        {
            throw new InvalidDataException($"The document has neither '{TopLevelHeading}' nor '{KeyOrderHeading}' with a table under it.");
        }

        return new DocumentedSet(sections, planned.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static void ReadRow(string[] cells, Dictionary<string, DocumentedSection> sections, List<string> planned)
    {
        var match = ObjectCell().Match(cells[0]);
        if (!match.Success)
        {
            throw new InvalidDataException($"The first cell of a row is not an object in code font: {cells[0]}");
        }

        var path = Normalize(match.Groups[1].Value);
        var rest = match.Groups[2].Value;
        if (rest.Contains("of a layout-only profile", StringComparison.Ordinal))
        {
            path += "#layoutOnlyField";
        }

        if (rest.Contains("planned", StringComparison.OrdinalIgnoreCase))
        {
            planned.Add(path);
            return;
        }

        var members = new List<string>();
        var nestedSections = new List<DocumentedSection>();
        foreach (var item in SplitTopLevel(CutProse(cells.Length > 1 ? cells[1] : string.Empty), ','))
        {
            var text = item.Trim();
            if (text.StartsWith("then ", StringComparison.Ordinal))
            {
                text = text[5..].TrimStart();
            }

            var read = MemberItem().Match(text);
            if (!read.Success)
            {
                throw new InvalidDataException($"The members of {Show(path)} cannot be read at '{text}' in: {cells[1]}");
            }

            var name = read.Groups[1].Value;
            var note = read.Groups[2].Success ? read.Groups[2].Value : null;
            if (note is not null && NameListShape().IsMatch(note))
            {
                members.Add(name);
                nestedSections.Add(new DocumentedSection(Join(path, name), NameListItem().Matches(note).Select(static m => m.Groups[1].Value).ToArray()));
            }
            else if (note is not null && note.Contains("planned", StringComparison.OrdinalIgnoreCase))
            {
                planned.Add(path.Length == 0 ? name : path + "/" + name);
            }
            else
            {
                members.Add(name);
            }
        }

        Add(sections, new DocumentedSection(path, members));
        foreach (var inner in nestedSections)
        {
            Add(sections, inner);
        }
    }

    private static void Add(Dictionary<string, DocumentedSection> sections, DocumentedSection section)
    {
        if (!sections.TryAdd(section.Path, section))
        {
            throw new InvalidDataException($"{Show(section.Path)} documented twice");
        }
    }

    private static string Join(string path, string name)
    {
        return path.Length == 0 ? name : path + "." + name;
    }

    private static string Show(string path)
    {
        return path.Length == 0 ? "(root)" : path;
    }

    // `fields[].x` is relative to the table; "[]" marks an array and is not part of the path.
    private static string Normalize(string raw)
    {
        var path = raw.Replace("[]", string.Empty, StringComparison.Ordinal);
        return path == "fields" || path.StartsWith("fields.", StringComparison.Ordinal) ? "table." + path : path;
    }

    private static string? CodeName(string cell)
    {
        var match = ObjectCell().Match(cell);
        return match.Success ? match.Groups[1].Value : null;
    }

    // The rows under a heading: the first table after it, without its header and separator rows, as arrays of trimmed cells.
    private static List<string[]>? Rows(string markdown, string heading)
    {
        var lines = markdown.Split('\n');
        var start = Array.FindIndex(lines, l => l.TrimEnd() == heading);
        if (start < 0)
        {
            return null;
        }

        var i = start + 1;
        while (i < lines.Length && !lines[i].StartsWith('|'))
        {
            if (lines[i].StartsWith('#'))
            {
                return null;
            }

            i++;
        }

        var rows = new List<string[]>();
        var seen = 0;
        for (; i < lines.Length && lines[i].StartsWith('|'); i++)
        {
            seen++;
            if (seen <= 2)
            {
                continue;
            }

            var cells = UnescapedBar().Split(lines[i].Trim().Trim('|')).Select(static c => c.Trim()).ToArray();
            rows.Add(cells);
        }

        return rows;
    }

    // Text before the first semicolon that is outside parentheses and code font.
    private static string CutProse(string cell)
    {
        var depth = 0;
        var code = false;
        for (var i = 0; i < cell.Length; i++)
        {
            switch (cell[i])
            {
                case '`':
                    code = !code;
                    break;
                case '(' when !code:
                    depth++;
                    break;
                case ')' when !code:
                    depth--;
                    break;
                case ';' when !code && depth == 0:
                    return cell[..i];
            }
        }

        return cell;
    }

    private static List<string> SplitTopLevel(string text, char separator)
    {
        var parts = new List<string>();
        var depth = 0;
        var code = false;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '`':
                    code = !code;
                    break;
                case '(' when !code:
                    depth++;
                    break;
                case ')' when !code:
                    depth--;
                    break;
                default:
                    if (text[i] == separator && !code && depth == 0)
                    {
                        parts.Add(text[start..i]);
                        start = i + 1;
                    }

                    break;
            }
        }

        parts.Add(text[start..]);
        return parts;
    }

    [GeneratedRegex(@"^`([^`]+)`(.*)$", RegexOptions.Singleline)]
    private static partial Regex ObjectCell();

    [GeneratedRegex(@"^`([^`]+)`\s*(?:\((.*)\))?\s*$", RegexOptions.Singleline)]
    private static partial Regex MemberItem();

    [GeneratedRegex(@"^`([^`]+)`(?:\s*,\s*`([^`]+)`)*$", RegexOptions.Singleline)]
    private static partial Regex NameListShape();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex NameListItem();

    [GeneratedRegex(@"(?<!\\)\|")]
    private static partial Regex UnescapedBar();
}

// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text.Json;

namespace squalor.squalify.Tests.Schema;

/// <summary>
/// The rules of <c>docs/descriptor.md</c> that a JSON Schema of the supported subset cannot express and that hold for every
/// descriptor the profiler writes: sums and equalities between members, lists that line up, pairs, the cross-member rules of the
/// null spellings, and the correlation of a layout-only descriptor (<c>layoutOnly</c> iff <c>hints</c> is null iff
/// <c>fingerprint.typed</c> is null iff every field is bare, and no evidence of the stage <c>type</c>). The check reads a descriptor
/// as JSON and never throws on a member that is missing: a descriptor that lacks one is a violation of the schema, and the check
/// skips what it cannot read, so the two checks report separately.
/// </summary>
internal static class DescriptorInvariants
{
    private static readonly string[] NameMembers = ["index", "name", "rawName", "nameHex", "nameParts"];

    /// <summary>Checks a descriptor.</summary>
    /// <param name="descriptor">The descriptor, as parsed JSON.</param>
    /// <returns>Every violation, as text (a pointer or a member name, then what is wrong); empty when the descriptor holds the rules.</returns>
    public static IReadOnlyList<string> Check(JsonElement descriptor)
    {
        var found = new List<string>();
        if (descriptor.ValueKind != JsonValueKind.Object)
        {
            found.Add("(root): a descriptor is an object");
            return found;
        }

        var context = new Context(descriptor, found);
        context.LayoutOnly();
        context.Format();
        context.Scope();
        context.Issues();
        context.Layout();
        context.Table();
        context.Hints();
        context.Binary();
        return found;
    }

    private sealed class Context
    {
        private readonly JsonElement _d;
        private readonly List<string> _v;

        public Context(JsonElement descriptor, List<string> violations)
        {
            _d = descriptor;
            _v = violations;
        }

        private bool Marked { get; set; }

        private static JsonElement? Get(JsonElement? parent, string name)
        {
            return parent is { ValueKind: JsonValueKind.Object } p && p.TryGetProperty(name, out var value) ? value : null;
        }

        private JsonElement? Get(params string[] path)
        {
            JsonElement? current = _d;
            foreach (var name in path)
            {
                current = Get(current, name);
            }

            return current;
        }

        private static long? Number(JsonElement? element)
        {
            return element is { ValueKind: JsonValueKind.Number } e && e.TryGetInt64(out var n) ? n : null;
        }

        private static bool IsNull(JsonElement? element) => element is { ValueKind: JsonValueKind.Null };

        private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);

        private static IEnumerable<JsonElement> Items(JsonElement? array)
        {
            return array is { ValueKind: JsonValueKind.Array } a ? a.EnumerateArray() : [];
        }

        // The layout-only correlation rule.

        public void LayoutOnly()
        {
            var marker = Get("layoutOnly");
            Marked = marker is { ValueKind: JsonValueKind.True };
            if (marker is not null && !Marked)
            {
                _v.Add("layoutOnly is written only as true, never false");
            }

            var hints = Get("hints");
            if (Marked && hints is not null && !IsNull(hints))
            {
                _v.Add("layoutOnly is true but hints is not null");
            }

            if (!Marked && IsNull(hints))
            {
                _v.Add("hints is null but layoutOnly is not true");
            }

            var typed = Get("fingerprint", "typed");
            if (Marked && typed is not null && !IsNull(typed))
            {
                _v.Add("layoutOnly is true but fingerprint.typed is not null");
            }

            if (!Marked && IsNull(typed))
            {
                _v.Add("fingerprint.typed is null but layoutOnly is not true");
            }

            var index = 0;
            foreach (var field in Items(Get("table", "fields")))
            {
                var bare = field.ValueKind == JsonValueKind.Object && field.EnumerateObject().All(static p => NameMembers.Contains(p.Name));
                if (Marked && !bare)
                {
                    _v.Add($"layoutOnly is true but /table/fields/{index} holds a member that is not a name");
                }

                if (!Marked && bare)
                {
                    _v.Add($"/table/fields/{index} is bare but layoutOnly is not true");
                }

                index++;
            }

            if (Marked && Items(Get("evidence")).Any(static e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("stage", out var s) && s.ValueKind == JsonValueKind.String && s.GetString() == "type"))
            {
                _v.Add("layoutOnly is true but the evidence holds an entry of the stage 'type'");
            }
        }

        // The family and its dialect, the encoding.

        public void Format()
        {
            var family = Get("format", "family") is { ValueKind: JsonValueKind.String } f ? f.GetString() : null;
            var dialect = Get("format", "dialect");
            if (family is not null && dialect is not null)
            {
                if (family == "delimited" && IsNull(dialect))
                {
                    _v.Add("format.dialect must not be null for the family delimited");
                }

                if (family != "delimited" && !IsNull(dialect))
                {
                    _v.Add("format.dialect must be null unless the family is delimited");
                }
            }

            if (family is not null && (Get("table") is not null) != (family == "delimited"))
            {
                _v.Add(family == "delimited" ? "table must be present for the family delimited" : "table is written only for the family delimited");
            }

            var tolerated = Get("encoding", "invalidBytesTolerated") is not null;
            var utf8Issue = Items(Get("issues")).Any(static i => i.ValueKind == JsonValueKind.Object && i.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String && c.GetString() == "invalidUtf8");
            if (Get("encoding") is not null && Get("issues") is not null && tolerated != utf8Issue)
            {
                _v.Add("encoding.invalidBytesTolerated is written exactly when the issue invalidUtf8 is");
            }
        }

        // The file and the byte limit.

        public void Scope()
        {
            var size = Number(Get("file", "size"));
            var hasHash = Get("file", "sha256") is not null;
            var scope = Get("scope");
            bool wholeFile;
            if (scope is null)
            {
                wholeFile = true;
            }
            else
            {
                var complete = Get("scope", "complete") is { ValueKind: JsonValueKind.True };
                var read = Number(Get("scope", "bytesRead"));
                wholeFile = complete;
                if (read is { } b && size is { } s)
                {
                    if (complete && b != s)
                    {
                        _v.Add($"scope.complete is true but bytesRead ({Text(b)}) is not file.size ({Text(s)})");
                    }

                    if (!complete && b == s)
                    {
                        _v.Add($"scope.complete is false but bytesRead ({Text(b)}) is file.size ({Text(s)})");
                    }
                }
            }

            if (Get("file") is not null)
            {
                if (hasHash && !wholeFile)
                {
                    _v.Add("file.sha256 is written although only a prefix was read");
                }

                if (!hasHash && wholeFile)
                {
                    _v.Add("file.sha256 is missing although the whole file was read");
                }
            }
        }

        // The lists of an issue line up.

        public void Issues()
        {
            var issues = Get("issues");
            if (issues is not { ValueKind: JsonValueKind.Array } array)
            {
                return;
            }

            var codes = array.EnumerateArray().Select(static i => Get(i, "code") is { ValueKind: JsonValueKind.String } c ? c.GetString()! : string.Empty).ToArray();
            if (!codes.SequenceEqual(codes.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            {
                _v.Add("issues are not sorted by code");
            }

            var index = 0;
            foreach (var issue in array.EnumerateArray())
            {
                var where = $"/issues/{index} ({codes[index]})";
                index++;
                var lines = Get(issue, "firstLines");
                var offsets = Get(issue, "offsets");
                var regions = Get(issue, "regions");
                if (regions is null)
                {
                    if (lines is not null && offsets is not null)
                    {
                        _v.Add($"{where} has both firstLines and offsets without regions");
                    }
                }
                else if (offsets is null)
                {
                    _v.Add($"{where} has regions without offsets");
                }
                else if (lines is null)
                {
                    _v.Add($"{where} has regions and offsets without firstLines");
                }
                else if (Count(lines) != Count(offsets) || Count(offsets) != Count(regions))
                {
                    _v.Add($"{where}: firstLines, offsets and regions must have the same length");
                }

                if (lines is { ValueKind: JsonValueKind.Array } l)
                {
                    var values = l.EnumerateArray().Select(static e => e.TryGetInt64(out var n) ? n : -1).ToArray();
                    var strict = regions is null;
                    if (!Ascending(values, strict))
                    {
                        _v.Add($"{where}: firstLines are not ascending");
                    }
                }

                if (offsets is { ValueKind: JsonValueKind.Array } o && !Ascending(o.EnumerateArray().Select(static e => e.TryGetInt64(out var n) ? n : -1).ToArray(), true))
                {
                    _v.Add($"{where}: offsets are not ascending");
                }
            }
        }

        private static int Count(JsonElement? array)
        {
            return array is { ValueKind: JsonValueKind.Array } a ? a.GetArrayLength() : -1;
        }

        private static bool Ascending(long[] values, bool strict)
        {
            for (var i = 1; i < values.Length; i++)
            {
                if (strict ? values[i] <= values[i - 1] : values[i] < values[i - 1])
                {
                    return false;
                }
            }

            return true;
        }

        // Regions: pairs, children inside their block, aligned lists.

        public void Layout()
        {
            var index = 0;
            foreach (var region in Items(Get("layout", "regions")))
            {
                Region(region, $"/layout/regions/{index}", null);
                index++;
            }

            var counts = Get("layout", "headerConvention", "counts");
            var fields = Get("table", "fields");
            if (counts is { ValueKind: JsonValueKind.Object } c && fields is { ValueKind: JsonValueKind.Array } f)
            {
                var total = c.EnumerateObject().Sum(static p => p.Value.TryGetInt64(out var n) ? n : 0);
                if (total != f.GetArrayLength())
                {
                    _v.Add($"layout.headerConvention.counts add up to {Text(total)}, not the number of fields ({f.GetArrayLength().ToString(CultureInfo.InvariantCulture)})");
                }
            }
        }

        private void Region(JsonElement region, string pointer, (long First, long Last, long Start, long End)? parent)
        {
            var lines = Pair(region, "lines", pointer);
            var bytes = Pair(region, "bytes", pointer);
            if (parent is { } p && lines is { } l && bytes is { } b && (l.First < p.First || l.Last > p.Last || b.First < p.Start || b.Last > p.End))
            {
                _v.Add($"{pointer} (lines {Text(l.First)}-{Text(l.Last)}, bytes {Text(b.First)}-{Text(b.Last)}) is not inside its block (lines {Text(p.First)}-{Text(p.Last)}, bytes {Text(p.Start)}-{Text(p.End)})");
            }

            if (Get(region, "keys") is { ValueKind: JsonValueKind.Array } keys && Get(region, "values") is { ValueKind: JsonValueKind.Array } values && keys.GetArrayLength() != values.GetArrayLength())
            {
                _v.Add($"{pointer}: values and keys must have the same length");
            }

            var index = 0;
            foreach (var child in Items(Get(region, "regions")))
            {
                Region(child, $"{pointer}/regions/{index}", lines is { } ll && bytes is { } bb ? (ll.First, ll.Last, bb.First, bb.Last) : null);
                index++;
            }
        }

        private (long First, long Last)? Pair(JsonElement region, string name, string pointer)
        {
            var value = Get(region, name);
            if (value is not { ValueKind: JsonValueKind.Array } array)
            {
                return null;
            }

            var numbers = array.EnumerateArray().Select(static e => e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var n) ? (long?)n : null).ToArray();
            if (numbers.Length != 2 || numbers.Any(static n => n is null) || numbers[0] > numbers[1])
            {
                _v.Add($"{pointer}/{name} must be a pair [first, last] of integers, first not after last");
                return null;
            }

            return (numbers[0]!.Value, numbers[1]!.Value);
        }

        // The table: row accounting, the row classes, the counts of every field.

        public void Table()
        {
            var table = Get("table");
            if (table is not { ValueKind: JsonValueKind.Object })
            {
                return;
            }

            var rows = Number(Get("table", "rows"));
            var ragged = Items(Get("issues")).Where(static i => Get(i, "code") is { ValueKind: JsonValueKind.String } c && c.GetString() == "raggedRows").Select(static i => Number(Get(i, "count")) ?? 0).Sum();
            var fields = Get("table", "fields") is { ValueKind: JsonValueKind.Array } a ? a.EnumerateArray().ToArray() : [];
            var allNullRows = RowClasses(rows, ragged, fields.Length);

            var maxDistinct = Number(Get("limits", "nullSpellings", "maxDistinctOther"));
            var index = 0;
            foreach (var field in fields)
            {
                Field(field, index++, rows, ragged, allNullRows, maxDistinct);
            }
        }

        private long? RowClasses(long? rows, long ragged, int fieldCount)
        {
            var classes = Get("table", "rowClasses");
            if (classes is not { ValueKind: JsonValueKind.Object })
            {
                return null;
            }

            var list = Items(Get("table", "rowClasses", "classes")).ToArray();
            var count = Number(Get(classes, "count"));
            if (count is { } c && c != list.Length)
            {
                _v.Add($"table.rowClasses.count ({Text(c)}) is not the number of classes listed ({list.Length.ToString(CultureInfo.InvariantCulture)})");
            }

            var truncated = Number(Get(classes, "truncated")) ?? 0;
            var sum = list.Sum(static e => Number(Get(e, "count")) ?? 0);
            if (rows is { } r && r != sum + truncated + ragged)
            {
                _v.Add($"table.rows ({Text(r)}) is not the sum of the class counts ({Text(sum)}) plus truncated ({Text(truncated)}) plus ragged ({Text(ragged)})");
            }

            var ordered = list.Select(static e => (Count: Number(Get(e, "count")) ?? 0, Id: Get(e, "id") is { ValueKind: JsonValueKind.String } s ? s.GetString()! : string.Empty)).ToArray();
            var expected = ordered.OrderByDescending(static o => o.Count).ThenBy(static o => o.Id, StringComparer.Ordinal).ToArray();
            if (!ordered.SequenceEqual(expected))
            {
                _v.Add("table.rowClasses.classes are not sorted by count descending, then by id");
            }

            long? allNullClass = null;
            for (var i = 0; i < list.Length; i++)
            {
                var ranges = Get(list[i], "fields");
                var previousEnd = -2L;
                foreach (var range in Items(ranges))
                {
                    var ends = range.ValueKind == JsonValueKind.Array ? range.EnumerateArray().Select(static e => e.TryGetInt64(out var n) ? n : -1).ToArray() : [];
                    if (ends.Length != 2 || ends[0] > ends[1])
                    {
                        _v.Add($"/table/rowClasses/classes/{i}/fields: each range must be a pair [first, last]");
                        continue;
                    }

                    if (ends[0] <= previousEnd + 1)
                    {
                        _v.Add($"/table/rowClasses/classes/{i}/fields: ranges must be ascending and not adjacent");
                    }

                    if (fieldCount > 0 && ends[1] > fieldCount - 1)
                    {
                        _v.Add($"/table/rowClasses/classes/{i}/fields: [{Text(ends[0])}, {Text(ends[1])}] is past the last field ({(fieldCount - 1).ToString(CultureInfo.InvariantCulture)})");
                    }

                    previousEnd = ends[1];
                }

                if (ranges is { ValueKind: JsonValueKind.Array } rangeList && rangeList.GetArrayLength() == 0)
                {
                    allNullClass = Number(Get(list[i], "count"));
                }
            }

            var allNull = Number(Get(classes, "allNullRows"));
            if (allNull is { } n)
            {
                if (allNullClass is { } k && k != n)
                {
                    _v.Add($"table.rowClasses.allNullRows ({Text(n)}) is not the count of the class with no non-null field ({Text(k)})");
                }

                if (allNullClass is null && n != 0 && truncated == 0)
                {
                    _v.Add($"table.rowClasses.allNullRows is {Text(n)} but no class has no non-null field and nothing was truncated");
                }

                var spellings = Get(classes, "allNullRowSpellings") is { ValueKind: JsonValueKind.Object } m ? m.EnumerateObject().Sum(static p => p.Value.TryGetInt64(out var v) ? v : 0) : 0;
                var mixed = Number(Get(classes, "allNullRowSpellingsMixed")) ?? 0;
                var other = Number(Get(classes, "allNullRowSpellingsOther")) ?? 0;
                if (spellings + mixed + other != n)
                {
                    _v.Add($"table.rowClasses: allNullRowSpellings ({Text(spellings)}) plus Mixed ({Text(mixed)}) plus Other ({Text(other)}) is not allNullRows ({Text(n)})");
                }
            }

            SpellingTriple(
                "/table/rowClasses: ",
                "allNullRowSpellingsOther",
                "allNullRowSpellingsOtherDistinct",
                "allNullRowSpellingsOtherDistinctCapped",
                Number(Get(classes, "allNullRowSpellingsOther")),
                Number(Get(classes, "allNullRowSpellingsOtherDistinct")),
                Get(classes, "allNullRowSpellingsOtherDistinctCapped") is { ValueKind: JsonValueKind.True },
                requireDistinctWhenOther: true,
                maxDistinct: Number(Get("limits", "nullSpellings", "maxDistinctOther")));
            return allNull;
        }

        private void Field(JsonElement field, int index, long? rows, long ragged, long? allNullRows, long? maxDistinct)
        {
            var at = $"/table/fields/{index.ToString(CultureInfo.InvariantCulture)}";
            var count = Number(Get(field, "count"));
            var nulls = Number(Get(field, "nulls"));
            var excluding = Number(Get(field, "nullsExcludingAllNullRows"));
            if (count is null || nulls is null || excluding is null)
            {
                // A bare field (layout-only), or a full field that lacks its counts: the correlation rule and the schema report it.
                return;
            }

            if (rows is { } r && count != r - ragged)
            {
                _v.Add($"{at}: count ({Text(count.Value)}) is not table.rows ({Text(r)}) minus the ragged records ({Text(ragged)})");
            }

            if (allNullRows is { } a && nulls - excluding != a)
            {
                _v.Add($"{at}: nulls ({Text(nulls.Value)}) minus nullsExcludingAllNullRows ({Text(excluding.Value)}) is not allNullRows ({Text(a)})");
            }

            if (Get(field, "typeVotes") is { ValueKind: JsonValueKind.Object } votes)
            {
                var sum = votes.EnumerateObject().Sum(static p => p.Value.TryGetInt64(out var n) ? n : 0);
                if (sum != count - nulls)
                {
                    _v.Add($"{at}: the type votes ({Text(sum)}) are not count minus nulls ({Text(count.Value - nulls.Value)})");
                }
            }

            if (Get(field, "nullTokens") is { ValueKind: JsonValueKind.Object } tokens)
            {
                var sum = tokens.EnumerateObject().Sum(static p => p.Value.TryGetInt64(out var n) ? n : 0);
                if (sum != nulls)
                {
                    _v.Add($"{at}: nullTokens add up to {Text(sum)}, not nulls ({Text(nulls.Value)})");
                }
            }

            var other = Number(Get(field, "nullSpellingsOther"));
            var spellings = Get(field, "nullSpellings") is { ValueKind: JsonValueKind.Object } spelled ? spelled.EnumerateObject().Sum(static p => p.Value.TryGetInt64(out var n) ? n : 0) : 0;
            if (spellings + (other ?? 0) != nulls)
            {
                _v.Add($"{at}: nullSpellings ({Text(spellings)}) plus nullSpellingsOther ({Text(other ?? 0)}) is not nulls ({Text(nulls.Value)})");
            }

            SpellingTriple(
                at + ": ",
                "nullSpellingsOther",
                "nullSpellingsOtherDistinct",
                "nullSpellingsOtherDistinctCapped",
                other,
                Number(Get(field, "nullSpellingsOtherDistinct")),
                Get(field, "nullSpellingsOtherDistinctCapped") is { ValueKind: JsonValueKind.True },
                requireDistinctWhenOther: false,
                maxDistinct);
        }

        // Distinct is present exactly when Other is (a field leaves both out when Other is 0; the table always writes both), Distinct <= Other,
        // at least 1 when Other is positive, and Capped implies Distinct == limits.nullSpellings.maxDistinctOther.
        private void SpellingTriple(string where, string otherName, string distinctName, string cappedName, long? other, long? distinct, bool capped, bool requireDistinctWhenOther, long? maxDistinct)
        {
            if (requireDistinctWhenOther)
            {
                // The table writes both members always, 0 when nothing was dropped.
                var o = other ?? 0;
                var d = distinct ?? 0;
                if (o == 0 && d > 0)
                {
                    _v.Add($"{where}{distinctName} ({Text(d)}) is positive but {otherName} is 0");
                }

                if (o > 0 && d == 0)
                {
                    _v.Add($"{where}{otherName} ({Text(o)}) is positive but {distinctName} is 0");
                }
            }
            else
            {
                if (other is not null && distinct is null)
                {
                    _v.Add($"{where}{otherName} is present but {distinctName} is not");
                }

                if (distinct is not null && other is null)
                {
                    _v.Add($"{where}{distinctName} is present but {otherName} is not");
                }
            }

            if (other is { } oo && distinct is { } dd && dd > oo)
            {
                _v.Add($"{where}{distinctName} ({Text(dd)}) is more than {otherName} ({Text(oo)})");
            }

            if (maxDistinct is { } max)
            {
                if (capped && (distinct ?? 0) != max)
                {
                    _v.Add($"{where}{cappedName} is true but {distinctName} ({Text(distinct ?? 0)}) is not limits.nullSpellings.maxDistinctOther ({Text(max)})");
                }

                if (!capped && (distinct ?? 0) > max)
                {
                    _v.Add($"{where}{distinctName} ({Text(distinct ?? 0)}) is more than limits.nullSpellings.maxDistinctOther ({Text(max)}) without {cappedName}");
                }
            }
        }

        // The hints repeat the table and the file.

        public void Hints()
        {
            var hints = Get("hints");
            if (hints is not { ValueKind: JsonValueKind.Object })
            {
                return;
            }

            var fields = Get("table", "fields") is { ValueKind: JsonValueKind.Array } a ? a.EnumerateArray().ToArray() : null;
            var names = fields?.Select(static f => Get(f, "name") is { ValueKind: JsonValueKind.String } n ? n.GetString() : null).ToArray();
            var named = names is not null && names.Length > 0 && names.All(static n => n is not null);
            var headerFields = Get(hints, "headerFields");
            if (fields is not null && headerFields is not null)
            {
                var expected = named ? names!.Cast<string>().ToArray() : null;
                var actual = headerFields is { ValueKind: JsonValueKind.Array } h ? h.EnumerateArray().Select(static e => e.GetString() ?? string.Empty).ToArray() : null;
                if (expected is null ? actual is not null : actual is null || !actual.SequenceEqual(expected, StringComparer.Ordinal))
                {
                    _v.Add("hints.headerFields is not the field names");
                }

                var prefix = Get(hints, "headerPrefix") is { ValueKind: JsonValueKind.Array } p ? p.EnumerateArray().Select(static e => e.GetString() ?? string.Empty).ToArray() : null;
                if (actual is not null && (prefix is null || !prefix.SequenceEqual(actual.Take(5), StringComparer.Ordinal)))
                {
                    _v.Add("hints.headerPrefix is not the first 5 header fields");
                }
            }

            if (fields is not null && Get(hints, "nonNullFields") is { ValueKind: JsonValueKind.Array } nonNull && fields.All(static f => Number(Get(f, "count")) is not null))
            {
                var expected = new List<long>();
                for (var i = 0; i < fields.Length; i++)
                {
                    if (Number(Get(fields[i], "nullsExcludingAllNullRows")) == 0 && Number(Get(fields[i], "count")) > Number(Get(fields[i], "nulls")))
                    {
                        expected.Add(i);
                    }
                }

                var actual = nonNull.EnumerateArray().Select(static e => e.TryGetInt64(out var n) ? n : -1).ToArray();
                if (!actual.SequenceEqual(expected))
                {
                    _v.Add($"hints.nonNullFields is [{string.Join(",", actual)}], not the fields with no null outside the all-null rows ([{string.Join(",", expected)}])");
                }
            }

            var tableCount = Get("table", "fieldCount");
            var hintCount = Get(hints, "fieldCount");
            if (tableCount is not null && hintCount is not null && (Number(Get(tableCount, "min")) != Number(Get(hintCount, "min")) || Number(Get(tableCount, "max")) != Number(Get(hintCount, "max"))))
            {
                _v.Add("hints.fieldCount is not table.fieldCount");
            }

            if (tableCount is null && hintCount is not null && !IsNull(hintCount))
            {
                _v.Add("hints.fieldCount must be null for a file that is not a table");
            }

            if (Get("file", "name") is { ValueKind: JsonValueKind.String } fileName && Get(hints, "fileNameShape") is { ValueKind: JsonValueKind.String } shape)
            {
                var expected = ShapeMask.Of(fileName.GetString()!);
                if (shape.GetString() != expected)
                {
                    _v.Add($"hints.fileNameShape is not the shape of file.name ({expected})");
                }
            }

            var delimiter = Get("format", "dialect", "delimiter");
            if (delimiter is not null && Get(hints, "delimiter") is { } hd && JsonSerializer.Serialize(hd) != JsonSerializer.Serialize(delimiter.Value))
            {
                _v.Add("hints.delimiter is not format.dialect.delimiter");
            }

            var convention = Get("layout", "headerConvention", "dominant");
            if (Get(hints, "headerConvention") is { } hc && JsonSerializer.Serialize(hc) != JsonSerializer.Serialize(convention is null ? JsonSerializer.SerializeToElement<string?>(null) : convention.Value))
            {
                _v.Add("hints.headerConvention is not layout.headerConvention.dominant");
            }
        }

        // The byte histogram.

        public void Binary()
        {
            var bytes = Get("binary", "bytes");
            if (bytes is not { ValueKind: JsonValueKind.Object })
            {
                return;
            }

            if (Get(bytes, "histogram") is not { ValueKind: JsonValueKind.Array } histogram)
            {
                return;
            }

            var counts = histogram.EnumerateArray().Select(static e => e.TryGetInt64(out var n) ? n : 0).ToArray();
            if (counts.Length != 256)
            {
                _v.Add("binary.bytes.histogram must have 256 counts");
                return;
            }

            if (Number(Get(bytes, "nul")) is { } nul && nul != counts[0])
            {
                _v.Add($"binary.bytes.nul ({Text(nul)}) is not histogram[0] ({Text(counts[0])})");
            }

            var printable = counts.Skip(0x20).Take(0x7F - 0x20).Sum();
            if (Number(Get(bytes, "printableAscii")) is { } p && p != printable)
            {
                _v.Add($"binary.bytes.printableAscii ({Text(p)}) is not the sum of histogram[0x20..0x7E] ({Text(printable)})");
            }

            var high = counts.Skip(0x80).Sum();
            if (Number(Get(bytes, "high")) is { } h && h != high)
            {
                _v.Add($"binary.bytes.high ({Text(h)}) is not the sum of histogram[0x80..0xFF] ({Text(high)})");
            }

            var read = Number(Get("scope", "bytesRead")) ?? Number(Get("file", "size"));
            if (read is { } total && counts.Sum() != total)
            {
                _v.Add($"binary.bytes.histogram adds up to {Text(counts.Sum())}, not the {Text(total)} bytes read");
            }
        }
    }
}

// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text;

namespace squalor.squalify;

/// <summary>
/// A minimal compact JSON writer for the fingerprint projections. It does not use the descriptor writer, whose
/// escaping of unassigned code points follows the runtime's Unicode tables: a hash must not change with the runtime.
/// Here a string is written as it is, except that <c>"</c> and <c>\</c> are escaped, a character below U+0020 is
/// written as <c>\u00xx</c> (lower-case hex), and a lone surrogate is written as U+FFFD. Everything else is
/// literal UTF-8.
/// </summary>
internal sealed class ProjectionJson
{
    private static readonly UTF8Encoding Utf8 = new(false, false);

    private readonly StringBuilder _text = new();
    private readonly Stack<bool> _needsComma = new();
    private bool _afterName;

    /// <summary>Starts an object.</summary>
    /// <returns>This writer.</returns>
    public ProjectionJson BeginObject()
    {
        Separate();
        _text.Append('{');
        _needsComma.Push(false);
        return this;
    }

    /// <summary>Ends an object.</summary>
    /// <returns>This writer.</returns>
    public ProjectionJson EndObject()
    {
        _needsComma.Pop();
        _text.Append('}');
        return this;
    }

    /// <summary>Starts an array.</summary>
    /// <returns>This writer.</returns>
    public ProjectionJson BeginArray()
    {
        Separate();
        _text.Append('[');
        _needsComma.Push(false);
        return this;
    }

    /// <summary>Ends an array.</summary>
    /// <returns>This writer.</returns>
    public ProjectionJson EndArray()
    {
        _needsComma.Pop();
        _text.Append(']');
        return this;
    }

    /// <summary>Writes a member name.</summary>
    /// <param name="name">The name.</param>
    /// <returns>This writer.</returns>
    public ProjectionJson Name(string name)
    {
        Separate();
        AppendString(name);
        _text.Append(':');
        _afterName = true;
        return this;
    }

    /// <summary>Writes a string value.</summary>
    /// <param name="value">The value.</param>
    /// <returns>This writer.</returns>
    public ProjectionJson String(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Separate();
        AppendString(value);
        return this;
    }

    /// <summary>Writes a string value, or null.</summary>
    /// <param name="value">The value.</param>
    /// <returns>This writer.</returns>
    public ProjectionJson StringOrNull(string? value)
    {
        return value is null ? Null() : String(value);
    }

    /// <summary>Writes a Boolean value.</summary>
    /// <param name="value">The value.</param>
    /// <returns>This writer.</returns>
    public ProjectionJson Bool(bool value)
    {
        Separate();
        _text.Append(value ? "true" : "false");
        return this;
    }

    /// <summary>Writes a null value.</summary>
    /// <returns>This writer.</returns>
    public ProjectionJson Null()
    {
        Separate();
        _text.Append("null");
        return this;
    }

    /// <summary>Writes an array of strings.</summary>
    /// <param name="values">The values.</param>
    /// <returns>This writer.</returns>
    public ProjectionJson Strings(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        BeginArray();
        foreach (var value in values)
        {
            String(value);
        }

        return EndArray();
    }

    /// <summary>The bytes written so far, UTF-8 without a BOM.</summary>
    /// <returns>The bytes.</returns>
    public byte[] ToBytes()
    {
        return Utf8.GetBytes(_text.ToString());
    }

    private void Separate()
    {
        if (_afterName)
        {
            _afterName = false;
            return;
        }

        if (_needsComma.Count > 0)
        {
            if (_needsComma.Peek())
            {
                _text.Append(',');
            }

            _needsComma.Pop();
            _needsComma.Push(true);
        }
    }

    private void AppendString(string value)
    {
        _text.Append('"');
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '"' || c == '\\')
            {
                _text.Append('\\').Append(c);
            }
            else if (c < ' ')
            {
                _text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            }
            else if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                _text.Append(c).Append(value[i + 1]);
                i++;
            }
            else if (char.IsSurrogate(c))
            {
                _text.Append("\\ufffd");
            }
            else
            {
                _text.Append(c);
            }
        }

        _text.Append('"');
    }
}

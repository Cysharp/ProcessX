using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace ProcessX;

/// <summary>
/// Builds the script of <c>sh($"type {path} | findstr TODO")</c>. Literal text is the script, written by the author
/// and passed as it is; every interpolation hole is a value and gets quoted for the shell that will run the script,
/// so it can not break out of its argument. <c>{value:raw}</c> inserts a value as script text instead.
/// </summary>
[InterpolatedStringHandler]
public struct ShellInterpolatedStringHandler
{
    /// <summary>Format specifier of a hole that is inserted without quoting: <c>{value:raw}</c>.</summary>
    public const string RawFormat = "raw";

    // Not a ref struct so that `await` can be used inside interpolation holes.
    readonly List<(string Text, bool IsValue)> parts;

    public ShellInterpolatedStringHandler(int literalLength, int formattedCount)
    {
        parts = new List<(string, bool)>(formattedCount * 2 + 1);
    }

    public void AppendLiteral(string value)
    {
        parts.Add((value, false));
    }

    /// <summary>A string hole is one quoted value. null adds nothing.</summary>
    public void AppendFormatted(string? value)
    {
        if (value == null) return;
        parts.Add((value, true));
    }

    /// <summary>A sequence becomes one quoted value per element, separated by spaces.</summary>
    public void AppendFormatted(IEnumerable<string>? values)
    {
        AppendSequence(values, isValue: true);
    }

    public void AppendFormatted<T>(T value)
    {
        AppendFormatted(value, null);
    }

    public void AppendFormatted<T>(T value, string? format)
    {
        var isValue = format != RawFormat;
        if (!isValue) format = null;

        switch (value)
        {
            case null:
                break;
            case string s:
                parts.Add((s, isValue));
                break;
            case IEnumerable<string> sequence:
                AppendSequence(sequence, isValue);
                break;
            case IFormattable formattable:
                // culture must not change what a command receives
                parts.Add((formattable.ToString(format, CultureInfo.InvariantCulture), isValue));
                break;
            default:
                if (value.ToString() is { } text) parts.Add((text, isValue));
                break;
        }
    }

    void AppendSequence(IEnumerable<string>? values, bool isValue)
    {
        if (values == null) return;

        var first = true;
        foreach (var value in values)
        {
            if (!first) parts.Add((" ", false));
            first = false;
            parts.Add((value, isValue));
        }
    }

    /// <summary>A plain string is script text, passed as it is.</summary>
    public static implicit operator ShellInterpolatedStringHandler(string script)
    {
        var handler = new ShellInterpolatedStringHandler(script.Length, 0);
        handler.AppendLiteral(script);
        return handler;
    }

    internal string Build(ShellQuoting quoting)
    {
        if (parts.Count == 1 && !parts[0].IsValue) return parts[0].Text;

        var sb = new StringBuilder();
        foreach (var (text, isValue) in parts)
        {
            sb.Append(isValue ? ShellQuote.Quote(text, quoting) : text);
        }
        return sb.ToString();
    }
}

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace ProcessX;

/// <summary>
/// Turns <c>run($"git commit -m {message}")</c> into an argument list without going through a shell.
/// Literal text is split on whitespace; every interpolation hole stays inside the argument it was written in,
/// so its value never needs escaping.
/// </summary>
[InterpolatedStringHandler]
public struct CommandInterpolatedStringHandler
{
    // Not a ref struct so that `await` can be used inside interpolation holes.
    readonly List<string> tokens;
    readonly StringBuilder current;
    bool hasCurrent;
    char quote;

    public CommandInterpolatedStringHandler(int literalLength, int formattedCount)
    {
        tokens = new List<string>(formattedCount + 4);
        current = new StringBuilder(literalLength);
        hasCurrent = false;
        quote = '\0';
    }

    /// <summary>
    /// Literal part: whitespace separates arguments, "..." and '...' group text that contains whitespace.
    /// Backslash is an ordinary character, so Windows paths can be written as is.
    /// </summary>
    public void AppendLiteral(string value)
    {
        foreach (var c in value)
        {
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
                hasCurrent = true; // "" is an empty argument
            }
            else if (char.IsWhiteSpace(c))
            {
                Flush();
            }
            else
            {
                current.Append(c);
                hasCurrent = true;
            }
        }
    }

    /// <summary>A string hole is always a single argument (or part of one). null adds nothing.</summary>
    public void AppendFormatted(string? value)
    {
        if (value == null) return;
        current.Append(value);
        hasCurrent = true;
    }

    /// <summary>A sequence expands to one argument per element. An empty sequence adds nothing.</summary>
    public void AppendFormatted(IEnumerable<string>? values)
    {
        if (values == null) return;

        var first = true;
        foreach (var value in values)
        {
            if (!first) Flush();
            first = false;
            current.Append(value);
            hasCurrent = true;
        }
    }

    public void AppendFormatted<T>(T value)
    {
        AppendFormatted(value, null);
    }

    public void AppendFormatted<T>(T value, string? format)
    {
        switch (value)
        {
            case null:
                break;
            case string s:
                AppendFormatted(s);
                break;
            case IEnumerable<string> sequence:
                AppendFormatted(sequence);
                break;
            case IFormattable formattable:
                // culture must not change what a command receives
                AppendFormatted(formattable.ToString(format, CultureInfo.InvariantCulture));
                break;
            default:
                AppendFormatted(value.ToString());
                break;
        }
    }

    void Flush()
    {
        if (hasCurrent)
        {
            tokens.Add(current.ToString());
            current.Clear();
            hasCurrent = false;
        }
    }

    internal (string FileName, string[] Arguments) Build()
    {
        if (quote != '\0')
        {
            throw new FormatException("The command has an unterminated quote (" + quote + ").");
        }

        Flush();

        if (tokens.Count == 0)
        {
            throw new ArgumentException("The command is empty.");
        }

        return (tokens[0], tokens.GetRange(1, tokens.Count - 1).ToArray());
    }

    /// <summary>A plain string is treated as literal text: split on whitespace, quotes group.</summary>
    public static implicit operator CommandInterpolatedStringHandler(string command)
    {
        var handler = new CommandInterpolatedStringHandler(command.Length, 0);
        handler.AppendLiteral(command);
        return handler;
    }
}

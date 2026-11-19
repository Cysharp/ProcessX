using System.Text;

namespace ProcessX;

/// <summary>How a value is quoted when it is put into a script.</summary>
public enum ShellQuoting
{
    /// <summary>sh, bash, zsh and other POSIX-like shells: <c>'value'</c>, a quote inside is written <c>'\''</c>.</summary>
    Posix,

    /// <summary>PowerShell: <c>'value'</c>, a quote inside is doubled.</summary>
    PowerShell,

    /// <summary>
    /// cmd.exe: <c>"value"</c>. cmd.exe has no way to quote every character, so a value that contains
    /// <c>"</c>, <c>%</c> or a line break is rejected with an <see cref="ArgumentException"/>.
    /// </summary>
    Cmd,
}

internal static class ShellQuote
{
    public static string Quote(string value, ShellQuoting quoting)
    {
        return quoting switch
        {
            ShellQuoting.Posix => QuotePosix(value),
            ShellQuoting.PowerShell => QuotePowerShell(value),
            ShellQuoting.Cmd => QuoteCmd(value),
            _ => throw new ArgumentOutOfRangeException(nameof(quoting)),
        };
    }

    // Nothing is special inside single quotes, and a single quote can not appear there:
    // close the string, write an escaped quote, open it again.
    static string QuotePosix(string value)
    {
        return "'" + value.Replace("'", "'\\''") + "'";
    }

    // Inside single quotes only the quote itself is special, and it is written twice.
    // PowerShell also accepts the typographic quotes as single quotes.
    static string QuotePowerShell(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('\'');
        foreach (var c in value)
        {
            if (c is '\'' or '\u2018' or '\u2019' or '\u201A' or '\u201B')
            {
                sb.Append(c);
            }
            sb.Append(c);
        }
        sb.Append('\'');
        return sb.ToString();
    }

    // Inside double quotes, cmd.exe leaves & | < > ^ ( ) alone. What it can not neutralize:
    //   "   ends the quoted part
    //   %   %NAME% is expanded even inside quotes
    //   line break   ends the command
    // Guessing an escape for those would be a false promise (this is the "BatBadBut" class of bugs), so they are refused.
    static string QuoteCmd(string value)
    {
        foreach (var c in value)
        {
            if (c is '"' or '%' or '\r' or '\n')
            {
                throw new ArgumentException(
                    "The value can not be passed safely through cmd.exe because it contains a double quote, a percent sign or a line break: "
                    + value + Environment.NewLine
                    + "Use run($\"...\") to start the process without a shell, another shell (Zx.shell), "
                    + "or {value:raw} if the text is meant to be part of the script.");
            }
        }

        // The program that receives "C:\dir\" would read \" as an escaped quote; doubling the trailing
        // backslashes makes the closing quote a closing quote again.
        var trailingBackslashes = 0;
        for (var i = value.Length - 1; i >= 0 && value[i] == '\\'; i--)
        {
            trailingBackslashes++;
        }

        return "\"" + value + new string('\\', trailingBackslashes) + "\"";
    }
}

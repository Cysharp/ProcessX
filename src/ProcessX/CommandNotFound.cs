using System.ComponentModel;

namespace ProcessX;

/// <summary>
/// "The system cannot find the file specified" says nothing about the usual reason: the command is not an executable.
/// <c>run</c> starts an executable directly, so shell built-ins (echo, cd) and, on Windows, batch files (npm.cmd)
/// do not start. This rewrites the message to say so and to point at <c>sh</c>.
/// </summary>
internal static class CommandNotFound
{
    // ERROR_FILE_NOT_FOUND / ENOENT, ERROR_PATH_NOT_FOUND
    const int FileNotFound = 2;
    const int PathNotFound = 3;

    // internal commands of cmd.exe
    static readonly HashSet<string> CmdBuiltIns = new(StringComparer.OrdinalIgnoreCase)
    {
        "assoc", "break", "call", "cd", "chdir", "cls", "color", "copy", "date", "del", "dir", "echo", "endlocal",
        "erase", "exit", "for", "ftype", "goto", "if", "md", "mkdir", "mklink", "move", "path", "pause", "popd",
        "prompt", "pushd", "rd", "rem", "ren", "rename", "rmdir", "set", "setlocal", "shift", "start", "time",
        "title", "type", "ver", "verify", "vol",
    };

    /// <summary>Returns an exception with a helpful message, or null when the failure is something else.</summary>
    public static Win32Exception? Describe(string fileName, bool viaShell, string? workingDirectory, Win32Exception exception)
    {
        if (exception.NativeErrorCode is not (FileNotFound or PathNotFound)) return null;

        // a missing working directory fails the same way, and the original message names it
        if (workingDirectory != null && !Directory.Exists(workingDirectory)) return null;

        return new Win32Exception(exception.NativeErrorCode, CreateMessage(fileName, viaShell));
    }

    static string CreateMessage(string fileName, bool viaShell)
    {
        if (viaShell)
        {
            return $"Shell not found: '{fileName}'. Set Zx.shell to a shell that is installed.";
        }

        const string UseShell = "Use sh(\"...\") or await \"...\" to run it through the shell.";

        if (OperatingSystem.IsWindows())
        {
            if (CmdBuiltIns.Contains(fileName))
            {
                return $"'{fileName}' is a built-in command of cmd.exe, not an executable, so run can not start it. {UseShell}";
            }

            if (Which.TryGetPath(fileName, out var path) && Path.GetExtension(path).ToLowerInvariant() is ".cmd" or ".bat")
            {
                return $"'{fileName}' is a batch file ({path}), which only cmd.exe can run, so run can not start it. {UseShell}";
            }
        }

        return $"Command not found: '{fileName}'. run starts an executable directly, without a shell: check that it is installed and on PATH. "
            + $"If it is a shell built-in, alias or function, {char.ToLowerInvariant(UseShell[0])}{UseShell[1..]}";
    }
}

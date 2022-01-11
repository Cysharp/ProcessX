// This class is borrowd from https://github.com/mayuki/Chell

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Zx
{
    internal static class Which
    {
        public static bool TryGetPath(string commandName, out string matchedPath)
        {
            var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(isWindows ? ';' : ':');
            var pathExts = Array.Empty<string>();

            if (isWindows)
            {
                paths = paths.Prepend(Environment.CurrentDirectory).ToArray();
                pathExts = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD;.VBS;.VBE;.JS;.JSE;.WSF;.WSH;.MSC").Split(';');
            }

            foreach (var path in paths)
            {
                // /path/to/foo.ext
                foreach (var ext in pathExts)
                {
                    var fullPath = Path.Combine(path, $"{commandName}{ext}");
                    if (File.Exists(fullPath))
                    {
                        matchedPath = fullPath;
                        return true;
                    }
                }

                // /path/to/foo
                {
                    var fullPath = Path.Combine(path, commandName);
                    if (File.Exists(fullPath))
                    {
                        matchedPath = fullPath;
                        return true;
                    }
                }
            }

            matchedPath = string.Empty;
            return false;
        }
    }
}
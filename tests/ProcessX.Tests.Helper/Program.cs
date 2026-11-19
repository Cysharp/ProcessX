// Child process used by the tests: a portable stand-in for echo/cat/exit/sleep.
// usage: dotnet ProcessX.Tests.Helper.dll <mode> [args...]

// always UTF-8, so tests do not depend on the console code page of the machine
Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.InputEncoding = System.Text.Encoding.UTF8;

var mode = args.Length == 0 ? "" : args[0];
var rest = args.AsSpan(1).ToArray();

switch (mode)
{
    case "args": // one argument per line
        foreach (var arg in rest)
        {
            Console.Out.WriteLine(arg);
        }
        return 0;

    case "exit": // exit <code> [stderr message]
        if (rest.Length > 1)
        {
            Console.Error.WriteLine(rest[1]);
        }
        return int.Parse(rest[0]);

    case "both": // both <stdout line> <stderr line>
        Console.Out.WriteLine(rest[0]);
        Console.Error.WriteLine(rest[1]);
        return 0;

    case "upper": // stdin -> stdout, upper-cased
        while (Console.In.ReadLine() is { } line)
        {
            Console.Out.WriteLine(line.ToUpperInvariant());
        }
        return 0;

    case "count": // number of lines on stdin
        var count = 0;
        while (Console.In.ReadLine() != null)
        {
            count++;
        }
        Console.Out.WriteLine(count);
        return 0;

    case "bytes": // bytes <size> [exit code]: binary stdout, the byte at position i is i % 256
        using (var stdout = Console.OpenStandardOutput())
        {
            var size = int.Parse(rest[0]);
            var chunk = new byte[65536];
            for (var written = 0; written < size;)
            {
                var length = Math.Min(chunk.Length, size - written);
                for (var i = 0; i < length; i++)
                {
                    chunk[i] = (byte)((written + i) % 256);
                }
                stdout.Write(chunk, 0, length);
                written += length;
            }
        }
        if (rest.Length > 1)
        {
            Console.Error.WriteLine("failed after writing");
            return int.Parse(rest[1]);
        }
        return 0;

    case "cat": // stdin -> stdout, byte for byte
        using (var stdin = Console.OpenStandardInput())
        using (var stdout = Console.OpenStandardOutput())
        {
            stdin.CopyTo(stdout);
        }
        return 0;

    case "sleep": // sleep <milliseconds>
        Thread.Sleep(int.Parse(rest[0]));
        return 0;

    case "ticks": // ticks <count> <interval ms>: one line per interval, flushed
        for (var i = 0; i < int.Parse(rest[0]); i++)
        {
            Console.Out.WriteLine(i);
            Console.Out.Flush();
            Thread.Sleep(int.Parse(rest[1]));
        }
        return 0;

    case "env": // value of an environment variable
        Console.Out.WriteLine(Environment.GetEnvironmentVariable(rest[0]) ?? "(null)");
        return 0;

    case "cwd":
        Console.Out.WriteLine(Environment.CurrentDirectory);
        return 0;

    case "pid-and-sleep": // prints own pid, then sleeps <milliseconds>
        Console.Out.WriteLine(Environment.ProcessId);
        Console.Out.Flush();
        Thread.Sleep(int.Parse(rest[0]));
        return 0;

    case "terminate": // terminate <timeout seconds>: a script that cleans up when terminateToken is canceled
        ProcessX.Zx.terminateTimeout = TimeSpan.FromSeconds(double.Parse(rest[0]));
        var token = ProcessX.Zx.terminateToken;
        Console.Out.WriteLine("ready");
        Console.Out.Flush();
        try
        {
            await Task.Delay(60000, token);
            Console.Out.WriteLine("not canceled");
        }
        catch (OperationCanceledException)
        {
            Console.Out.WriteLine("canceled");
        }
        finally
        {
            Console.Out.WriteLine("cleanup");
        }
        return Environment.ExitCode;

    case "terminate-hang": // terminate-hang <timeout seconds>: a script that never looks at the token again
        ProcessX.Zx.terminateTimeout = TimeSpan.FromSeconds(double.Parse(rest[0]));
        _ = ProcessX.Zx.terminateToken;
        Console.Out.WriteLine("ready");
        Console.Out.Flush();
        Thread.Sleep(60000);
        Console.Out.WriteLine("not terminated");
        return 0;

    case "graceful": // graceful <marker file> <cleanup ms>: on a signal, cleans up for a while, writes the marker, exits 0
    {
        var signaled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<System.Runtime.InteropServices.PosixSignalContext> onSignal = context =>
        {
            context.Cancel = true;
            signaled.TrySetResult();
        };
        using var sigInt = System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGINT, onSignal);
        using var sigQuit = System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGQUIT, onSignal);
        using var sigTerm = System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGTERM, onSignal);

        Console.Out.WriteLine("child-ready");
        Console.Out.Flush();

        await signaled.Task.WaitAsync(TimeSpan.FromSeconds(60));
        await Task.Delay(int.Parse(rest[1]));
        File.WriteAllText(rest[0], "cleaned up");
        return 0;
    }

    case "stubborn": // stubborn: ignores every signal and never ends by itself; prints its pid
    {
        Action<System.Runtime.InteropServices.PosixSignalContext> ignore = context => context.Cancel = true;
        using var sigInt = System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGINT, ignore);
        using var sigQuit = System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGQUIT, ignore);
        using var sigTerm = System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGTERM, ignore);

        Console.Out.WriteLine("child-ready " + Environment.ProcessId);
        Console.Out.Flush();
        Thread.Sleep(60000);
        return 0;
    }

    // terminate-child <child mode> <grace seconds, -1 = default> <child args...>
    // A script that runs a child with terminateToken; reports how the child ended and how long stopping it took.
    case "terminate-child":
    {
        ProcessX.Zx.quiet = true;
        ProcessX.Zx.terminateTimeout = TimeSpan.FromSeconds(30);

        var self = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var grace = int.Parse(rest[1]);
        var command = ProcessX.Zx.run($"dotnet {self} {rest[0]} {rest.AsSpan(2).ToArray()}").WithCancellation(ProcessX.Zx.terminateToken);
        if (grace >= 0)
        {
            command = command.GracePeriod(grace);
        }

        var stopwatch = new System.Diagnostics.Stopwatch();
        try
        {
            await foreach (var line in command)
            {
                if (line.StartsWith("child-ready"))
                {
                    stopwatch.Start();
                    Console.Out.WriteLine(line.Replace("child-ready", "ready")); // now the test may send the signal
                    Console.Out.Flush();
                }
            }
            Console.Out.WriteLine("completed");
        }
        catch (OperationCanceledException)
        {
            Console.Out.WriteLine("canceled");
        }
        Console.Out.WriteLine("stopped-in-ms=" + stopwatch.ElapsedMilliseconds);
        return 0;
    }

    default:
        Console.Error.WriteLine("unknown mode: " + mode);
        return 100;
}

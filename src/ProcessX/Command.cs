using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace ProcessX;

/// <summary>
/// A command that has not started yet. It starts when awaited (or enumerated by <c>await foreach</c>),
/// and each await starts a new process.
/// </summary>
public sealed class Command : IAsyncEnumerable<string>
{
    readonly string fileName;
    readonly string[] arguments;
    readonly string? rawArguments;

    Command? upstream;
    bool noThrow;
    bool? quiet; // null: follow Zx.quiet
    bool inherit;
    TimeSpan? timeout;
    CancellationToken cancellationToken;
    string? workingDirectory;
    Dictionary<string, string?>? environment;
    Encoding? encoding;
    string? inputText;
    ReadOnlyMemory<byte>? inputBytes;
    Stream? inputStream;

    bool HasInput => inputText != null || inputBytes != null || inputStream != null;

    // the process is a shell that runs the real processes as its children
    readonly bool viaShell;
    TimeSpan? gracePeriod;

    internal Command(string fileName, string[] arguments, string? rawArguments, bool viaShell = false)
    {
        this.fileName = fileName;
        this.arguments = arguments;
        this.rawArguments = rawArguments;
        this.viaShell = viaShell;
    }

    public string FileName => fileName;

    /// <summary>Arguments passed to the process. Empty for a cmd.exe shell command, which uses a raw command line.</summary>
    public IReadOnlyList<string> Arguments => arguments;

    Command With(Action<Command> configure)
    {
        var clone = (Command)MemberwiseClone();
        configure(clone);
        return clone;
    }

    /// <summary>Do not throw <see cref="ProcessErrorException"/> on a non-zero exit code; check <see cref="CommandResult.ExitCode"/> instead.</summary>
    public Command NoThrow() => With(x => x.noThrow = true);

    /// <summary>
    /// Do not echo the output of this command to the console. This overrides <see cref="Zx.quiet"/>,
    /// so <c>Quiet(false)</c> echoes the output of this command even when everything else is quiet.
    /// </summary>
    public Command Quiet(bool quiet = true) => With(x => x.quiet = quiet);

    /// <summary>
    /// Do not capture output: the process uses this process's stdout/stderr directly,
    /// so colors, progress bars and interactive prompts work. The result has no output, only an exit code.
    /// </summary>
    public Command Inherit() => With(x => x.inherit = true);

    /// <summary>Kill the process tree and throw <see cref="TimeoutException"/> when it does not finish in time.</summary>
    public Command Timeout(TimeSpan timeout) => With(x => x.timeout = timeout);

    /// <summary>Kill the process tree and throw <see cref="TimeoutException"/> when it does not finish in time.</summary>
    public Command Timeout(int seconds) => Timeout(TimeSpan.FromSeconds(seconds));

    /// <summary>
    /// How long a process that has been asked to stop may take before it is killed, when the command is canceled,
    /// times out, or its <c>await foreach</c> is left. Default is 3 seconds; <see cref="TimeSpan.Zero"/> kills immediately.
    /// </summary>
    /// <remarks>
    /// A process is "asked to stop" in two cases. After Ctrl+C (with <see cref="Zx.terminateToken"/>), the system has
    /// already delivered the signal to it. Otherwise, on Linux and macOS, SIGTERM is sent to a process started by
    /// <see cref="Zx.run(CommandInterpolatedStringHandler)"/>. In every other case there is no way to ask
    /// (Windows can not signal a single process, and a signaled shell would abandon its children),
    /// so the process tree is killed immediately.
    /// </remarks>
    public Command GracePeriod(TimeSpan gracePeriod) => With(x => x.gracePeriod = gracePeriod);

    /// <inheritdoc cref="GracePeriod(TimeSpan)"/>
    public Command GracePeriod(int seconds) => GracePeriod(TimeSpan.FromSeconds(seconds));

    /// <summary>Kill the process tree and throw <see cref="OperationCanceledException"/> when canceled.</summary>
    public Command WithCancellation(CancellationToken cancellationToken) => With(x => x.cancellationToken = cancellationToken);

    /// <summary>Working directory of this command. Default is the current directory of the script (see <see cref="Zx.cd(string)"/>).</summary>
    public Command Cwd(string workingDirectory) => With(x => x.workingDirectory = workingDirectory);

    /// <summary>Set (or with null, remove) an environment variable for this command, on top of the environment of the script.</summary>
    public Command SetEnv(string name, string? value) => With(x =>
    {
        x.environment = x.environment == null ? new() : new(x.environment);
        x.environment[name] = value;
    });

    /// <summary>Encoding used to decode stdout and stderr, and to encode the text given to <see cref="Input(string)"/>.</summary>
    public Command WithEncoding(Encoding encoding) => With(x => x.encoding = encoding);

    /// <summary>
    /// Write <paramref name="text"/> to stdin of the process and close it, instead of inheriting stdin of the script.
    /// The text is written as it is (no newline is added). Use this for values that must not appear
    /// in the command line, such as passwords: <c>run($"docker login -u {user} --password-stdin").Input(password)</c>.
    /// On a pipeline, the input goes to its first command.
    /// </summary>
    public Command Input(string text) => With(x =>
    {
        x.inputText = text;
        x.inputBytes = null;
        x.inputStream = null;
    });

    /// <summary>
    /// Write <paramref name="bytes"/> (a byte[] converts implicitly) to stdin of the process and close it,
    /// instead of inheriting stdin of the script. The bytes are not copied; they are read each time the command runs.
    /// </summary>
    public Command Input(ReadOnlyMemory<byte> bytes) => With(x =>
    {
        x.inputText = null;
        x.inputBytes = bytes;
        x.inputStream = null;
    });

    /// <summary>
    /// Copy <paramref name="stream"/> to stdin of the process and close it, instead of inheriting stdin of the script.
    /// The stream is read once, so such a command can only run once.
    /// </summary>
    public Command Input(Stream stream) => With(x =>
    {
        x.inputText = null;
        x.inputBytes = null;
        x.inputStream = stream;
    });

    /// <summary>Connect stdout of this command to stdin of <paramref name="next"/> with an OS pipe (no shell involved).</summary>
    public Command Pipe(Command next) => this | next;

    /// <summary>
    /// Connect stdout of <paramref name="left"/> to stdin of <paramref name="right"/> with an OS pipe (no shell involved).
    /// Options such as NoThrow/Quiet/Timeout of the last command apply to the whole pipeline,
    /// and the pipeline fails when any command in it fails.
    /// </summary>
    public static Command operator |(Command left, Command right)
    {
        return right.With(x => x.upstream = right.upstream == null ? left : left | right.upstream);
    }

    public TaskAwaiter<CommandResult> GetAwaiter() => ExecuteAsync().GetAwaiter();

    /// <summary>Start the command and capture its output.</summary>
    public async Task<CommandResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var stdOut = new List<string>();
        var stdErr = new List<string>();

        await using var execution = new Execution(this, stdErr, cancellationToken);
        execution.Start();

        await foreach (var line in execution.ReadLinesAsync().ConfigureAwait(false))
        {
            if (line.StandardError)
            {
                execution.AddError(line.Content);
            }
            else
            {
                stdOut.Add(line.Content);
                if (execution.Echo) Console.Out.WriteLine(line.Content);
            }
        }

        var exitCode = await execution.WaitForExitAsync().ConfigureAwait(false);
        return CreateResult(exitCode, stdOut.ToArray(), execution.GetErrors());
    }

    /// <summary>
    /// Start the command and read stdout line by line as it is produced.
    /// stderr is echoed (unless quiet) and kept for <see cref="ProcessErrorException"/>.
    /// </summary>
    public async IAsyncEnumerator<string> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        var stdErr = new List<string>();

        await using var execution = new Execution(this, stdErr, cancellationToken);
        execution.Start();

        await foreach (var line in execution.ReadLinesAsync().ConfigureAwait(false))
        {
            if (line.StandardError)
            {
                execution.AddError(line.Content);
            }
            else
            {
                yield return line.Content;
            }
        }

        var exitCode = await execution.WaitForExitAsync().ConfigureAwait(false);
        CreateResult(exitCode, [], execution.GetErrors());
    }

    /// <summary>
    /// Start the command and return stdout as raw bytes, for commands that write binary data
    /// (<c>git archive</c>, <c>curl</c>, image tools). Nothing is decoded or split into lines.
    /// stderr is still read as text: echoed (unless quiet) and kept for <see cref="ProcessErrorException"/>.
    /// </summary>
    public async Task<byte[]> ToBytesAsync(CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await ToStreamAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    /// <summary>
    /// Start the command and copy stdout as raw bytes to <paramref name="destination"/>, without holding it in memory.
    /// The result has the exit code and stderr, and no stdout.
    /// </summary>
    public async Task<CommandResult> ToStreamAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        var stdErr = new List<string>();

        await using var execution = new Execution(this, stdErr, cancellationToken, rawOutput: true);
        execution.Start();

        await execution.CopyOutputAsync(destination).ConfigureAwait(false);

        var exitCode = await execution.WaitForExitAsync().ConfigureAwait(false);
        return CreateResult(exitCode, [], execution.GetErrors());
    }

    CommandResult CreateResult(int exitCode, string[] stdOut, string[] stdErr)
    {
        var result = new CommandResult(exitCode, stdOut, stdErr);
        if (exitCode != 0 && !noThrow)
        {
            throw new ProcessErrorException(ToString(), result);
        }
        return result;
    }

    /// <summary>The command line formatted for display, for example in error messages.</summary>
    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (var command in Flatten())
        {
            if (sb.Length != 0) sb.Append(" | ");
            sb.Append(command.fileName);
            if (command.rawArguments != null)
            {
                sb.Append(' ').Append(command.rawArguments);
            }
            else
            {
                foreach (var argument in command.arguments)
                {
                    sb.Append(' ');
                    if (argument.Length == 0 || argument.Any(char.IsWhiteSpace))
                    {
                        sb.Append('"').Append(argument).Append('"');
                    }
                    else
                    {
                        sb.Append(argument);
                    }
                }
            }
        }
        return sb.ToString();
    }

    // upstream first
    List<Command> Flatten()
    {
        var list = new List<Command>();
        for (var command = this; command != null; command = command.upstream)
        {
            list.Add(command);
        }
        list.Reverse();
        return list;
    }

    ProcessStartInfo CreateStartInfo()
    {
        var startInfo = new ProcessStartInfo(fileName) { UseShellExecute = false };

        if (rawArguments != null)
        {
            startInfo.Arguments = rawArguments;
        }
        else
        {
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        if (workingDirectory != null)
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        if (environment != null)
        {
            foreach (var item in environment)
            {
                startInfo.Environment[item.Key] = item.Value;
            }
        }

        // A script that dies (Ctrl+C, exception, kill) must not leave its children running.
        // Not available on macOS.
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsAndroid())
        {
            startInfo.KillOnParentExit = true;
        }

        return startInfo;
    }

    /// <summary>One run of a command (or pipeline). Disposing it kills whatever is still running.</summary>
    sealed class Execution : IAsyncDisposable
    {
        readonly Command command;
        readonly List<string> errors;
        readonly CancellationToken callerToken;
        readonly CancellationTokenSource timeoutSource = new();
        readonly CancellationTokenSource linkedSource;
        readonly List<Process> processes = new();
        readonly List<Task> errorPumps = new();
        volatile Exception? inputError;

        static readonly TimeSpan DefaultGracePeriod = TimeSpan.FromSeconds(3);
        static readonly TimeSpan KillSettleTimeout = TimeSpan.FromSeconds(5);

        // stdout of the last command is copied as bytes instead of being read as lines
        readonly bool rawOutput;

        // Stopwatch timestamp taken right after the processes started
        long startedAt;

        public Execution(Command command, List<string> errors, CancellationToken callerToken, bool rawOutput = false)
        {
            this.command = command;
            this.errors = errors;
            this.callerToken = callerToken;
            this.rawOutput = rawOutput;
            this.linkedSource = CancellationTokenSource.CreateLinkedTokenSource(command.cancellationToken, callerToken, timeoutSource.Token);
            this.Echo = !(command.quiet ?? Zx.quiet);
        }

        public bool Echo { get; }

        CancellationToken Token => linkedSource.Token;

        bool IsTimeout => timeoutSource.IsCancellationRequested
            && !callerToken.IsCancellationRequested
            && !command.cancellationToken.IsCancellationRequested;

        TimeoutException CreateTimeoutException() => new("Command timed out after " + command.timeout + ": " + command);

        public void Start()
        {
            var chain = command.Flatten();
            var inputSource = FindInput(chain);

            SafeFileHandle? stdin = null;
            SafeFileHandle? inputWriter = null;
            try
            {
                if (inputSource != null)
                {
                    SafeFileHandle.CreateAnonymousPipe(out stdin, out inputWriter);
                }

                for (var i = 0; i < chain.Count; i++)
                {
                    var isLast = i == chain.Count - 1;
                    var current = chain[i];
                    var startInfo = current.CreateStartInfo();

                    SafeFileHandle? nextStdin = null;
                    SafeFileHandle? stdout = null;
                    try
                    {
                        if (stdin != null)
                        {
                            startInfo.StandardInputHandle = stdin;
                        }

                        if (!isLast)
                        {
                            SafeFileHandle.CreateAnonymousPipe(out nextStdin, out stdout);
                            startInfo.StandardOutputHandle = stdout;
                            startInfo.RedirectStandardError = true;
                        }
                        else if (rawOutput)
                        {
                            // with Inherit, only stdout is taken; stderr stays on the console (progress bars)
                            startInfo.RedirectStandardOutput = true;
                            startInfo.RedirectStandardError = !command.inherit;
                        }
                        else if (!command.inherit)
                        {
                            startInfo.RedirectStandardOutput = true;
                            startInfo.RedirectStandardError = true;
                        }

                        if (current.encoding != null)
                        {
                            if (startInfo.RedirectStandardOutput) startInfo.StandardOutputEncoding = current.encoding;
                            if (startInfo.RedirectStandardError) startInfo.StandardErrorEncoding = current.encoding;
                        }

                        Process process;
                        try
                        {
                            process = Process.Start(startInfo) ?? throw new InvalidOperationException("Can't start process: " + current.fileName);
                        }
                        catch (Win32Exception ex) when (CommandNotFound.Describe(current.fileName, current.viaShell, current.workingDirectory, ex) is { } described)
                        {
                            throw described;
                        }
                        processes.Add(process);

                        // every stderr that ReadLinesAsync does not read needs its own reader
                        if (startInfo.RedirectStandardError && (!isLast || rawOutput))
                        {
                            errorPumps.Add(PumpErrorAsync(process));
                        }

                        if (inputWriter != null)
                        {
                            // The first process of the chain has the read end; feed it from another thread.
                            // Nothing waits for this task: it ends when the input ends or the process stops reading,
                            // and a source that never ends must not keep a finished command from completing.
                            var writer = inputWriter;
                            inputWriter = null;
                            _ = Task.Run(() => WriteInput(writer, inputSource!));
                        }
                    }
                    catch
                    {
                        nextStdin?.Dispose();
                        throw;
                    }
                    finally
                    {
                        // the child owns its copies now; ours must be closed or the pipe never reaches EOF
                        stdin?.Dispose();
                        stdout?.Dispose();
                    }

                    stdin = nextStdin;
                }
            }
            catch
            {
                inputWriter?.Dispose();
                KillAll();
                throw;
            }

            startedAt = Stopwatch.GetTimestamp();

            if (command.timeout is { } timeout)
            {
                timeoutSource.CancelAfter(timeout);
            }
        }

        // Input belongs to the first command of the chain; given to the last one, it means "input of the pipeline".
        static Command? FindInput(List<Command> chain)
        {
            Command? source = null;
            for (var i = 0; i < chain.Count; i++)
            {
                var current = chain[i];
                if (!current.HasInput) continue;

                if (i != 0 && i != chain.Count - 1)
                {
                    throw new InvalidOperationException("Input can not be given to a command in the middle of a pipeline, its stdin is the pipe: " + current.fileName);
                }
                if (source != null)
                {
                    throw new InvalidOperationException("Input is given twice in a pipeline.");
                }
                source = current;
            }
            return source;
        }

        // Blocking writes on a thread pool thread: anonymous pipes are synchronous handles on every platform.
        void WriteInput(SafeFileHandle writer, Command source)
        {
            try
            {
                // disposing the stream closes our end, which is what gives the process EOF
                using var pipe = new FileStream(writer, FileAccess.Write, bufferSize: 0);

                if (source.inputText != null)
                {
                    var bytes = (source.encoding ?? Console.InputEncoding).GetBytes(source.inputText);
                    pipe.Write(bytes);
                    return;
                }

                if (source.inputBytes is { } memory)
                {
                    pipe.Write(memory.Span);
                    return;
                }

                var buffer = new byte[81920];
                while (true)
                {
                    int read;
                    try
                    {
                        read = source.inputStream!.Read(buffer);
                    }
                    catch (Exception ex)
                    {
                        inputError = ex; // a failure of the caller's stream is the caller's error
                        return;
                    }

                    if (read == 0) return;
                    pipe.Write(buffer, 0, read);
                }
            }
            catch (IOException)
            {
                // the process exited or closed stdin without reading everything; its exit code tells the rest
            }
            catch (ObjectDisposedException)
            {
            }
        }

        // stderr of a command in the middle of a pipeline
        async Task PumpErrorAsync(Process process)
        {
            try
            {
                while (await process.StandardError.ReadLineAsync(Token).ConfigureAwait(false) is { } line)
                {
                    AddError(line);
                }
            }
            catch
            {
                // canceled or killed; the reader of the last command reports it
            }
        }

        public void AddError(string line)
        {
            lock (errors)
            {
                errors.Add(line);
            }
            if (Echo) Console.Error.WriteLine(line);
        }

        public string[] GetErrors()
        {
            lock (errors)
            {
                return errors.ToArray();
            }
        }

        public async Task CopyOutputAsync(Stream destination)
        {
            try
            {
                await processes[^1].StandardOutput.BaseStream.CopyToAsync(destination, Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (IsTimeout)
            {
                throw CreateTimeoutException();
            }
        }

        public async IAsyncEnumerable<ProcessOutputLine> ReadLinesAsync()
        {
            if (command.inherit || rawOutput) yield break;

            var enumerator = processes[^1].ReadAllLinesAsync(Token).GetAsyncEnumerator();
            try
            {
                while (true)
                {
                    bool hasNext;
                    try
                    {
                        hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (IsTimeout)
                    {
                        throw CreateTimeoutException();
                    }

                    if (!hasNext) break;
                    yield return enumerator.Current;
                }
            }
            finally
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>Returns the exit code of the pipeline: the last non-zero one, or 0.</summary>
        public async Task<int> WaitForExitAsync()
        {
            try
            {
                foreach (var process in processes)
                {
                    await process.WaitForExitAsync(Token).ConfigureAwait(false);
                }
                await Task.WhenAll(errorPumps).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (IsTimeout)
            {
                throw CreateTimeoutException();
            }

            if (inputError is { } error)
            {
                // the process only saw a truncated input, so its result means nothing
                throw new IOException("Failed to read the stream given to Input: " + error.Message, error);
            }

            var exitCode = 0;
            foreach (var process in processes)
            {
                if (process.ExitCode != 0) exitCode = process.ExitCode;
            }
            return exitCode;
        }

        void KillAll()
        {
            foreach (var process in processes)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        // a shell command is `shell -> real process`, killing only the shell would leave the real one
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // already gone
                }
            }
        }

        // Stops whatever is still running: ask, wait for the grace period, kill, and wait until it is really gone.
        async Task StopAsync()
        {
            var running = processes.Where(IsRunning).ToArray();
            if (running.Length == 0) return;

            var gracePeriod = command.gracePeriod ?? DefaultGracePeriod;
            if (gracePeriod > TimeSpan.Zero && RequestStop(running))
            {
                await WaitUntilExitedAsync(running, gracePeriod).ConfigureAwait(false);
            }

            KillAll();

            // the caller must not continue while the processes are still dying (files in use, ports bound)
            await WaitUntilExitedAsync(running, KillSettleTimeout).ConfigureAwait(false);
        }

        // Returns true when the processes have been asked to stop by themselves, so that waiting for them makes sense.
        bool RequestStop(Process[] running)
        {
            // Ctrl+C goes to every process of the console (or foreground process group): they already know.
            // Sending it again would be harmful, a second Ctrl+C means "stop right now" to many tools.
            if (TerminateSignal.WasDeliveredToProcessesStartedAt(startedAt)) return true;

            // Windows can not signal a single process.
            if (OperatingSystem.IsWindows()) return false;

            // A signaled shell exits and leaves its children behind, out of reach of the tree kill.
            foreach (var current in command.Flatten())
            {
                if (current.viaShell) return false;
            }

            var requested = false;
            foreach (var process in running)
            {
                try
                {
                    requested |= process.SafeHandle.Signal(PosixSignal.SIGTERM);
                }
                catch
                {
                    // already gone
                }
            }
            return requested;
        }

        static bool IsRunning(Process process)
        {
            try
            {
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        static async Task WaitUntilExitedAsync(Process[] processes, TimeSpan timeout)
        {
            using var source = new CancellationTokenSource(timeout);
            try
            {
                await Task.WhenAll(processes.Select(x => x.WaitForExitAsync(source.Token))).ConfigureAwait(false);
            }
            catch
            {
                // timed out, or a process is already disposed of by the system
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
            linkedSource.Cancel(); // release pumps that are still waiting for a line
            await Task.WhenAll(errorPumps).ConfigureAwait(false);
            foreach (var process in processes)
            {
                process.Dispose();
            }
            linkedSource.Dispose();
            timeoutSource.Dispose();
        }
    }
}

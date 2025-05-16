using Cysharp.Diagnostics;

// messages in ffmpeg are sent to stderr

var ffmpeg = @"D:\apps\ffmpeg\bin\ffmpeg.exe";
var inputFile = @"d:\temp\Zoom.mp4";
var command = $"{ffmpeg} -y -i {inputFile} -c:v libx264 -crf 70 -f mpegts -";

// throws with ExitCode 0
if (false)
{
    var r = await ProcessX.StartReadBinaryAsync(command);
}
// returns stdout (mp4) and stderr (messages)
else if (false)
{
    var (bytesTask, errorsTask) = ProcessX.GetDualAsyncEnumerableBinary(command);

    var bytes = new byte[1];
    var consumeStdOut = Task.Run(async () => bytes = await bytesTask);

    // get messages from stderr
    var errorBuffered = new List<string>();
    var consumeStdError = Task.Run(async () =>
    {
        await foreach (var item in errorsTask)
        {
            Console.WriteLine("STDERROR: " + item);
            errorBuffered.Add(item);
        }
    });

    await Task.WhenAll(consumeStdOut, consumeStdError);

    File.WriteAllBytes($"{inputFile}.out.mp4", bytes);
}
// throws with ExitCode -22
else if (true)
{
    var (bytesTask, errorsTask) = ProcessX.GetDualAsyncEnumerableBinary(command.Replace("mpegts", "mp4", StringComparison.InvariantCulture));

    // get messages from stderr
    var errorBuffered = new List<string>();
    var consumeStdError = Task.Run(async () =>
    {
        await foreach (var item in errorsTask)
        {
            errorBuffered.Add(item);
        }
    });

    try
    {
        await Task.WhenAll(bytesTask, consumeStdError);
    }
    catch (ProcessErrorException ex)
    {
        Console.WriteLine("ERROR, ExitCode: " + ex.ExitCode);
        Console.WriteLine(string.Join(Environment.NewLine, errorBuffered));
    }
}

Console.ReadLine();

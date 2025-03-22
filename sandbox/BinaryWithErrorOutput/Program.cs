using Cysharp.Diagnostics;

// messages in ffmpeg are sent to stderr

var ffmpeg = @"D:\apps\ffmpeg\bin\ffmpeg.exe";
var inputFile = @"d:\temp\Zoom.mp4";
var command = $"{ffmpeg} -i {inputFile} -c:v libx264 -crf 60 -f mpegts -";

// throws with ExitCode 0
if (false)
{
    var r = await ProcessX.StartReadBinaryAsync(command);
}
// returns stdout (mp4) and stderr (messages)
else if (false)
{
    var r = await ProcessX.StartReadBinaryWithErrOutAsync(command);
    File.WriteAllBytes($"{inputFile}.out.mp4", r.StdOut);
}
// throws with ExitCode -22
else if (true)
{
    var r2 = await ProcessX.StartReadBinaryWithErrOutAsync(command.Replace("mpegts", "mp4", StringComparison.InvariantCulture));
    File.WriteAllBytes($"{inputFile}.out.mp4", r2.StdOut);
}

Console.ReadLine();

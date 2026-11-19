using System.Text;
using Xunit;
using static ProcessX.Tests.TestSetup;
using static ProcessX.Zx;

namespace ProcessX.Tests;

public class BinaryTest
{
    // every byte value, including the ones a text reader would change: NUL, CR, LF, Ctrl+Z, invalid UTF-8
    static byte[] Pattern(int size)
    {
        var bytes = new byte[size];
        for (var i = 0; i < size; i++)
        {
            bytes[i] = (byte)(i % 256);
        }
        return bytes;
    }

    [Fact]
    public async Task ToBytesReturnsStdOutByteForByte()
    {
        var bytes = await run($"dotnet {Helper} bytes 100000").ToBytesAsync();

        Assert.Equal(Pattern(100000), bytes);
    }

    [Fact]
    public async Task EmptyOutputIsEmptyArray()
    {
        Assert.Empty(await run($"dotnet {Helper} bytes 0").ToBytesAsync());
    }

    [Fact]
    public async Task LargeBinaryRoundTripsThroughStdinAndStdout()
    {
        var source = Pattern(5_000_000);

        var bytes = await run($"dotnet {Helper} cat").Input(new MemoryStream(source)).ToBytesAsync();

        Assert.Equal(source.Length, bytes.Length);
        Assert.True(source.AsSpan().SequenceEqual(bytes));
    }

    [Fact]
    public async Task ToBytesWorksOnPipeline()
    {
        var bytes = await (run($"dotnet {Helper} bytes 70000") | run($"dotnet {Helper} cat")).ToBytesAsync();

        Assert.Equal(Pattern(70000), bytes);
    }

    [Fact]
    public async Task ToStreamCopiesStdOutAndReturnsTheRest()
    {
        var destination = new MemoryStream();

        var result = await run($"dotnet {Helper} both out err").ToStreamAsync(destination);

        Assert.Equal("out", Encoding.UTF8.GetString(destination.ToArray()).TrimEnd());
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("err", result.StdErr);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public async Task ToStreamCanWriteToFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "zx-test-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            await using (var file = File.Create(path))
            {
                await run($"dotnet {Helper} bytes 300000").ToStreamAsync(file);
            }

            Assert.Equal(Pattern(300000), await File.ReadAllBytesAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task NonZeroExitCodeThrows()
    {
        var ex = await Assert.ThrowsAsync<ProcessErrorException>(() => run($"dotnet {Helper} bytes 1000 3").ToBytesAsync());

        Assert.Equal(3, ex.ExitCode);
        Assert.Equal(["failed after writing"], ex.ErrorOutput);
    }

    [Fact]
    public async Task NoThrowReturnsExitCodeAndWhatWasWritten()
    {
        var destination = new MemoryStream();

        var result = await run($"dotnet {Helper} bytes 1000 3").NoThrow().ToStreamAsync(destination);

        Assert.Equal(3, result.ExitCode);
        Assert.Equal(Pattern(1000), destination.ToArray());

        Assert.Equal(Pattern(1000), await run($"dotnet {Helper} bytes 1000 3").NoThrow().ToBytesAsync());
    }

    [Fact]
    public async Task TimeoutAndCancellation()
    {
        await Assert.ThrowsAsync<TimeoutException>(() => run($"dotnet {Helper} sleep 60000").Timeout(2).ToBytesAsync());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run($"dotnet {Helper} sleep 60000").ToBytesAsync(cts.Token));
    }

    [Fact]
    public async Task LinesAreAvailableThroughAsyncLinq()
    {
        // Command is IAsyncEnumerable<string>, so System.Linq's ToArrayAsync already means "all lines";
        // that is why the byte version is not called ToArray
        string[] lines = await run($"dotnet {Helper} ticks 3 10").ToArrayAsync();

        Assert.Equal(["0", "1", "2"], lines);
    }
}

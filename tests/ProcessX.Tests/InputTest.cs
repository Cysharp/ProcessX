using System.Text;
using Xunit;
using static ProcessX.Tests.TestSetup;
using static ProcessX.Zx;

namespace ProcessX.Tests;

public class InputTest
{
    [Fact]
    public async Task TextGoesToStdin()
    {
        var result = await run($"dotnet {Helper} upper").Input("abc\ndef");

        Assert.Equal(["ABC", "DEF"], result.Lines);
    }

    [Fact]
    public async Task EmptyTextIsJustEndOfInput()
    {
        Assert.Equal("0", await run($"dotnet {Helper} count").Input(""));
    }

    [Fact]
    public async Task StreamGoesToStdin()
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("x\ny\n"));

        Assert.Equal("2", await run($"dotnet {Helper} count").Input(stream));
    }

    [Fact]
    public async Task BytesGoToStdinByteForByte()
    {
        // every byte value, including NUL, CR, LF and invalid UTF-8
        var source = Enumerable.Range(0, 100_000).Select(x => (byte)(x % 256)).ToArray();

        var command = run($"dotnet {Helper} cat").Input(source);

        Assert.Equal(source, await command.ToBytesAsync());
        Assert.Equal(source, await command.ToBytesAsync()); // unlike a stream, bytes can be sent again

        var slice = await run($"dotnet {Helper} cat").Input(source.AsMemory(10, 20)).ToBytesAsync();
        Assert.Equal(source[10..30], slice);

        Assert.Equal("0", await run($"dotnet {Helper} count").Input(Array.Empty<byte>()));
    }

    [Fact]
    public async Task EncodingAppliesToText()
    {
        var result = await run($"dotnet {Helper} upper").Input("日本語 abc").WithEncoding(Encoding.UTF8);

        Assert.Equal("日本語 ABC", result.StdOut);
    }

    [Fact]
    public async Task CommandWithTextInputCanRunAgain()
    {
        var command = run($"dotnet {Helper} upper").Input("again");

        Assert.Equal("AGAIN", await command);
        Assert.Equal("AGAIN", await command);
    }

    [Fact]
    public void InputIsNotPartOfCommandLine()
    {
        var command = run($"dotnet {Helper} upper").Input("secret-value");

        Assert.DoesNotContain("secret-value", command.ToString());
        Assert.DoesNotContain("secret-value", command.Arguments);
    }

    [Fact]
    public async Task InputGoesToFirstCommandOfPipeline()
    {
        // given to the pipeline as a whole
        Assert.Equal("3", await (run($"dotnet {Helper} upper") | run($"dotnet {Helper} count")).Input("a\nb\nc"));

        // given to the first command
        Assert.Equal("2", await (run($"dotnet {Helper} upper").Input("a\nb") | run($"dotnet {Helper} count")));

        var result = await (run($"dotnet {Helper} upper").Input("a\nb") | run($"dotnet {Helper} upper"));
        Assert.Equal(["A", "B"], result.Lines);
    }

    [Fact]
    public async Task InputThatCanNotBeDeliveredIsRejected()
    {
        var middle = run($"dotnet {Helper} args a") | run($"dotnet {Helper} upper").Input("x") | run($"dotnet {Helper} count");
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await middle);

        var twice = run($"dotnet {Helper} upper").Input("a") | run($"dotnet {Helper} count").Input("b");
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await twice);
    }

    [Fact]
    public async Task LargeInputAndOutputDoNotDeadlock()
    {
        var text = string.Join("\n", Enumerable.Range(0, 200_000).Select(x => "line " + x));

        var result = await run($"dotnet {Helper} upper").Input(text);

        Assert.Equal(200_000, result.Lines.Count);
        Assert.Equal("LINE 199999", result.Lines[^1]);
    }

    [Fact]
    public async Task ProcessThatDoesNotReadInputStillCompletes()
    {
        var text = new string('x', 5_000_000);

        Assert.Equal("done", await run($"dotnet {Helper} args done").Input(text));
    }

    [Fact]
    public async Task FailingStreamIsReported()
    {
        var ex = await Assert.ThrowsAsync<IOException>(async () => await run($"dotnet {Helper} count").Input(new FailingStream()));

        Assert.IsType<InvalidDataException>(ex.InnerException);
    }

    sealed class FailingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidDataException("broken source");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

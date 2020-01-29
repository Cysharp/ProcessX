using System;

namespace Cysharp.Diagnostics
{
    public class ProcessErrorException : Exception
    {
        public int ExitCode { get; }
        public string[] ErrorOutput { get; }

        public ProcessErrorException(int exitCode, string[] errorOutput)
            : base("Process returns error, ExitCode:" + exitCode + Environment.NewLine + string.Join(Environment.NewLine, errorOutput))
        {
            this.ExitCode = exitCode;
            this.ErrorOutput = errorOutput;
        }
    }
}
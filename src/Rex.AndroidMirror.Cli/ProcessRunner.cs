using System.Diagnostics;
using System.Text;

namespace Rex.AndroidMirror.Cli;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;
}

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        bool captureOutput = true,
        CancellationToken cancellationToken = default)
    {
        var start = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = captureOutput,
            RedirectStandardOutput = captureOutput,
            RedirectStandardError = captureOutput,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = start };
        if (!process.Start())
        {
            return new ProcessResult(-1, string.Empty, $"Could not start {fileName}.");
        }

        if (!captureOutput)
        {
            await process.WaitForExitAsync(cancellationToken);
            return new ProcessResult(process.ExitCode, string.Empty, string.Empty);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        return new ProcessResult(
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
    }

    public Task<ProcessResult> RunPowerShellAsync(
        string scriptPath,
        IEnumerable<string>? scriptArguments = null,
        bool captureOutput = true,
        CancellationToken cancellationToken = default)
    {
        var args = new List<string>
        {
            "-NoLogo",
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            scriptPath,
        };

        if (scriptArguments is not null)
        {
            args.AddRange(scriptArguments);
        }

        return RunAsync(
            "powershell.exe",
            args,
            Path.GetDirectoryName(scriptPath) ?? Directory.GetCurrentDirectory(),
            captureOutput,
            cancellationToken);
    }

    public async Task<int> OpenAsync(string target, string? workingDirectory = null)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = target,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
            UseShellExecute = true,
        });

        if (process is null)
        {
            return -1;
        }

        await Task.Yield();
        return 0;
    }

    public async Task<int> StartDetachedAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory)
    {
        var start = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start);
        await Task.Yield();
        return process is null ? -1 : 0;
    }
}

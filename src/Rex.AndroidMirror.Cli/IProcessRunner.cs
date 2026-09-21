namespace Rex.AndroidMirror.Cli;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        bool captureOutput = true,
        CancellationToken cancellationToken = default);

    Task<ProcessResult> RunPowerShellAsync(
        string scriptPath,
        IEnumerable<string>? scriptArguments = null,
        bool captureOutput = true,
        CancellationToken cancellationToken = default);

    Task<int> OpenAsync(string target, string? workingDirectory = null);
}

using System.Diagnostics;

namespace VisualNovelResourceTool.Core;

public static class ExternalProcess
{
    public static async Task<(int ExitCode, string Output, string Error)> RunAsync(ProcessStartInfo start, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动外部工具。");
        // Drain both pipes even after cancellation; wait for the killed child before
        // returning so callers may safely delete its working files.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        try { await process.WaitForExitAsync(token); }
        finally
        {
            if (!process.HasExited) { try { process.Kill(true); } catch (InvalidOperationException) { } }
            await process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
        }
        token.ThrowIfCancellationRequested();
        return (process.ExitCode, await stdout, await stderr);
    }
}

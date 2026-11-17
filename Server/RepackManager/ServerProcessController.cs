using System.Diagnostics;
using System.IO;

namespace MMOnsterpatch.RepackManager;

public sealed class ServerProcessController
{
    public string Name { get; }
    public string ExePath { get; }
    public string Arguments { get; }
    public string WorkingDirectory { get; }
    public Process? Process { get; private set; }

    public bool IsRunning => Process is { HasExited: false };

    public event Action<string>? Log;
    public event Action<ServerProcessController>? Exited;

    public ServerProcessController(string name, string exePath, string workingDirectory, string arguments = "")
    {
        Name = name;
        ExePath = exePath;
        WorkingDirectory = workingDirectory;
        Arguments = arguments;
    }

    public void Start(bool minimized)
    {
        if (IsRunning)
        {
            Log?.Invoke($"{Name} already running.");
            return;
        }

        bool looksLikePath = ExePath.Contains(Path.DirectorySeparatorChar) || ExePath.Contains(Path.AltDirectorySeparatorChar) || Path.IsPathRooted(ExePath);
        if (looksLikePath && !File.Exists(ExePath))
        {
            Log?.Invoke($"{Name} executable not found: {ExePath}");
            return;
        }

        if (!Directory.Exists(WorkingDirectory))
        {
            Log?.Invoke($"{Name} working directory not found: {WorkingDirectory}");
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = ExePath,
            Arguments = Arguments,
            WorkingDirectory = WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = minimized,
            WindowStyle = minimized ? ProcessWindowStyle.Minimized : ProcessWindowStyle.Normal
        };

        try
        {
            Process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            Process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Log?.Invoke($"{Name}: {e.Data}"); };
            Process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Log?.Invoke($"{Name} ERR: {e.Data}"); };
            Process.Exited += (_, _) => Exited?.Invoke(this);

            if (Process.Start())
            {
                Process.BeginOutputReadLine();
                Process.BeginErrorReadLine();
                Log?.Invoke($"{Name} started. PID {Process.Id}");
                if (!string.IsNullOrWhiteSpace(Arguments))
                    Log?.Invoke($"{Name} args: {Arguments}");
            }
            else
            {
                Log?.Invoke($"{Name} failed to start.");
            }
        }
        catch (Exception ex)
        {
            Log?.Invoke($"{Name} start failed: {ex.Message}");
        }
    }

    public void SendCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return;

        if (!IsRunning || Process is null)
        {
            Log?.Invoke($"{Name} is not running. Command not sent: {command}");
            return;
        }

        try
        {
            Process.StandardInput.WriteLine(command);
            Process.StandardInput.Flush();
            Log?.Invoke($"> {command}");
        }
        catch (Exception ex)
        {
            Log?.Invoke($"{Name} command send failed: {ex.Message}");
        }
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            Log?.Invoke($"{Name} is not running.");
            return;
        }

        try
        {
            try
            {
                Process!.StandardInput.WriteLine("/shutdown");
            }
            catch
            {
                // Not every service accepts stdin commands.
            }

            if (!Process!.WaitForExit(2500))
                Process.Kill(entireProcessTree: true);

            Log?.Invoke($"{Name} stopped.");
        }
        catch (Exception ex)
        {
            Log?.Invoke($"{Name} stop failed: {ex.Message}");
        }
    }

    public void Restart(bool minimized)
    {
        Stop();
        Start(minimized);
    }
}

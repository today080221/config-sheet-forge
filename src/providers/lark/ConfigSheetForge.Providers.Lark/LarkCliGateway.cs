using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ConfigSheetForge.Providers.Lark;

public sealed class LarkCliGateway
{
    private readonly string _requestedExecutable;

    public LarkCliGateway(string? executable = null)
    {
        _requestedExecutable = string.IsNullOrWhiteSpace(executable) ? "lark-cli" : executable.Trim();
    }

    public LarkCliResolvedCommand Resolve()
    {
        return LarkCliDiscovery.Resolve(_requestedExecutable);
    }

    public async Task<LarkCliResult> RunAsync(IEnumerable<string> args, string? workingDirectory, CancellationToken cancellationToken)
    {
        var resolved = Resolve();
        var startInfo = new ProcessStartInfo
        {
            FileName = resolved.FileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        foreach (var arg in resolved.PrefixArguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        var userArgs = args.ToList();
        foreach (var arg in userArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is FileNotFoundException)
        {
            return new LarkCliResult(-1, "", ex.Message, string.Join(" ", userArgs), resolved);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        return new LarkCliResult(process.ExitCode, stdout, stderr, string.Join(" ", userArgs), resolved);
    }
}

public static class LarkCliDiscovery
{
    private static readonly string[] WindowsExecutableExtensions = { ".cmd", ".exe", ".bat", ".com" };

    public static LarkCliResolvedCommand Resolve(string? requestedExecutable = null)
    {
        var requested = string.IsNullOrWhiteSpace(requestedExecutable) ? "lark-cli" : requestedExecutable.Trim();

        foreach (var candidate in BuildCandidateCommands(requested))
        {
            if (candidate.Exists())
            {
                return candidate;
            }
        }

        return new LarkCliResolvedCommand(requested, Array.Empty<string>(), requested, "unresolved");
    }

    private static IEnumerable<LarkCliResolvedCommand> BuildCandidateCommands(string requested)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in FromExplicitValue(requested, "configured"))
        {
            if (seen.Add(candidate.Fingerprint))
            {
                yield return candidate;
            }
        }

        var envPath = Environment.GetEnvironmentVariable("LARK_CLI_PATH");
        if (!string.IsNullOrWhiteSpace(envPath) && !string.Equals(envPath.Trim(), requested, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var candidate in FromExplicitValue(envPath.Trim(), "env:LARK_CLI_PATH"))
            {
                if (seen.Add(candidate.Fingerprint))
                {
                    yield return candidate;
                }
            }
        }

        if (IsBareCommand(requested))
        {
            foreach (var candidate in FromPath(requested, "PATH"))
            {
                if (seen.Add(candidate.Fingerprint))
                {
                    yield return candidate;
                }
            }

            foreach (var candidate in FromKnownNpmLocations(requested))
            {
                if (seen.Add(candidate.Fingerprint))
                {
                    yield return candidate;
                }
            }

            foreach (var candidate in FromNodeModuleFallback("npm-global-node-module"))
            {
                if (seen.Add(candidate.Fingerprint))
                {
                    yield return candidate;
                }
            }
        }
    }

    private static IEnumerable<LarkCliResolvedCommand> FromExplicitValue(string value, string source)
    {
        if (IsBareCommand(value))
        {
            yield break;
        }

        foreach (var expanded in ExpandWindowsSiblingCandidates(Environment.ExpandEnvironmentVariables(value)))
        {
            yield return ToCommand(expanded, source);
        }
    }

    private static IEnumerable<LarkCliResolvedCommand> FromPath(string command, string source)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in path.Split(Path.PathSeparator).Select(p => p.Trim().Trim('"')).Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            if (IsWindows())
            {
                foreach (var extension in PreferredWindowsExtensions(command))
                {
                    yield return ToCommand(Path.Combine(directory, command + extension), source);
                }
            }
            else
            {
                yield return ToCommand(Path.Combine(directory, command), source);
            }
        }
    }

    private static IEnumerable<LarkCliResolvedCommand> FromKnownNpmLocations(string command)
    {
        if (!IsWindows())
        {
            yield break;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
        {
            var npmDirectory = Path.Combine(appData, "npm");
            foreach (var extension in PreferredWindowsExtensions(command))
            {
                yield return ToCommand(Path.Combine(npmDirectory, command + extension), "npm-appdata");
            }
        }
    }

    private static IEnumerable<LarkCliResolvedCommand> FromNodeModuleFallback(string source)
    {
        foreach (var script in KnownRunJsLocations())
        {
            foreach (var node in ResolveNodeCandidates())
            {
                yield return new LarkCliResolvedCommand(node, new[] { script }, "node " + script, source);
            }
        }
    }

    private static IEnumerable<string> KnownRunJsLocations()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
        {
            yield return Path.Combine(appData, "npm", "node_modules", "@larksuite", "cli", "scripts", "run.js");
        }

        var npmRoot = TryRunSimpleCommand("npm", "root", "-g");
        if (!string.IsNullOrWhiteSpace(npmRoot))
        {
            yield return Path.Combine(npmRoot.Trim(), "@larksuite", "cli", "scripts", "run.js");
        }
    }

    private static IEnumerable<string> ResolveNodeCandidates()
    {
        if (IsWindows())
        {
            foreach (var node in FromPath("node", "PATH").Where(c => c.Exists()).Select(c => c.FileName))
            {
                yield return node;
            }

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                yield return Path.Combine(programFiles, "nodejs", "node.exe");
            }
        }
        else
        {
            yield return "node";
        }
    }

    private static LarkCliResolvedCommand ToCommand(string path, string source)
    {
        var extension = Path.GetExtension(path);
        if (IsWindows() && extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            var pwsh = ResolveShell("pwsh") ?? ResolveShell("powershell") ?? "powershell";
            return new LarkCliResolvedCommand(pwsh, new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", path }, path, source + ":ps1");
        }

        if (extension.Equals(".js", StringComparison.OrdinalIgnoreCase))
        {
            var node = ResolveShell("node") ?? "node";
            return new LarkCliResolvedCommand(node, new[] { path }, path, source + ":node");
        }

        return new LarkCliResolvedCommand(path, Array.Empty<string>(), path, source);
    }

    private static string? ResolveShell(string command)
    {
        return FromPath(command, "PATH").FirstOrDefault(c => c.Exists())?.FileName;
    }

    private static IEnumerable<string> ExpandWindowsSiblingCandidates(string path)
    {
        if (!IsWindows())
        {
            yield return path;
            yield break;
        }

        var extension = Path.GetExtension(path);
        if (!string.IsNullOrWhiteSpace(extension))
        {
            yield return path;
            yield break;
        }

        foreach (var candidateExtension in WindowsExecutableExtensions.Concat(new[] { ".ps1" }))
        {
            yield return path + candidateExtension;
        }
    }

    private static IEnumerable<string> PreferredWindowsExtensions(string command)
    {
        var extension = Path.GetExtension(command);
        if (!string.IsNullOrWhiteSpace(extension))
        {
            yield return "";
            yield break;
        }

        foreach (var extensionFromPath in WindowsExecutableExtensions)
        {
            yield return extensionFromPath;
        }

        yield return ".ps1";
    }

    private static string? TryRunSimpleCommand(string executable, params string[] args)
    {
        try
        {
            var command = FromPath(executable, "PATH").Concat(FromKnownNpmLocations(executable)).FirstOrDefault(c => c.Exists()) ??
                          new LarkCliResolvedCommand(executable, Array.Empty<string>(), executable, "unresolved");

            var startInfo = new ProcessStartInfo
            {
                FileName = command.FileName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            foreach (var prefix in command.PrefixArguments)
            {
                startInfo.ArgumentList.Add(prefix);
            }

            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return null;
            }

            var stdout = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(5000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best-effort cleanup only.
                }

                return null;
            }

            return process.ExitCode == 0 ? stdout.Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsBareCommand(string value)
    {
        return !Path.IsPathRooted(value) &&
               !value.Contains(Path.DirectorySeparatorChar) &&
               !value.Contains(Path.AltDirectorySeparatorChar);
    }

    private static bool IsWindows()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    }
}

public sealed class LarkCliResolvedCommand
{
    public LarkCliResolvedCommand(string fileName, IReadOnlyList<string> prefixArguments, string displayPath, string source)
    {
        FileName = fileName;
        PrefixArguments = prefixArguments;
        DisplayPath = displayPath;
        Source = source;
    }

    public string FileName { get; }
    public IReadOnlyList<string> PrefixArguments { get; }
    public string DisplayPath { get; }
    public string Source { get; }
    public string Fingerprint => FileName + "\n" + string.Join("\n", PrefixArguments);

    public bool Exists()
    {
        if (PrefixArguments.Count > 0 && PrefixArguments[0].EndsWith(".js", StringComparison.OrdinalIgnoreCase))
        {
            return File.Exists(FileName) && File.Exists(PrefixArguments[0]);
        }

        if (PrefixArguments.Count > 0 && PrefixArguments[^1].EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            return File.Exists(FileName) && File.Exists(PrefixArguments[^1]);
        }

        return File.Exists(FileName);
    }
}

public sealed class LarkCliResult
{
    public LarkCliResult(int exitCode, string stdout, string stderr, string command, LarkCliResolvedCommand resolvedCommand)
    {
        ExitCode = exitCode;
        Stdout = stdout;
        Stderr = stderr;
        Command = command;
        ResolvedCommand = resolvedCommand;
    }

    public int ExitCode { get; }
    public string Stdout { get; }
    public string Stderr { get; }
    public string Command { get; }
    public LarkCliResolvedCommand ResolvedCommand { get; }
    public bool Success => ExitCode == 0;
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ConfigSheetForge.Core;
using UnityEngine;

namespace ConfigSheetForge.Unity.Editor
{
    public static class ConfigSheetForgeEditorUtility
    {
        public static string BuildCommandLine(string executable, IEnumerable<string> args)
        {
            var builder = new StringBuilder();
            builder.Append(QuoteArgument(executable));
            foreach (var arg in args)
            {
                builder.Append(' ').Append(QuoteArgument(arg));
            }

            return builder.ToString();
        }

        public static string JoinArguments(IEnumerable<string> args)
        {
            var builder = new StringBuilder();
            foreach (var arg in args)
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(QuoteArgument(arg));
            }

            return builder.ToString();
        }

        public static string QuoteArgument(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "\"\"";
            }

            if (value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            {
                return value;
            }

            var builder = new StringBuilder();
            builder.Append('"');
            var backslashes = 0;
            foreach (var c in value)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (c == '"')
                {
                    builder.Append('\\', backslashes * 2 + 1);
                    builder.Append('"');
                    backslashes = 0;
                    continue;
                }

                builder.Append('\\', backslashes);
                backslashes = 0;
                builder.Append(c);
            }

            builder.Append('\\', backslashes * 2);
            builder.Append('"');
            return builder.ToString();
        }

        public static string ResolveExecutable(string executable)
        {
            if (string.IsNullOrWhiteSpace(executable))
            {
                return executable;
            }

            if (Path.IsPathRooted(executable) || executable.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) >= 0)
            {
                return executable;
            }

            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            var extensions = Application.platform == RuntimePlatform.WindowsEditor
                ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';')
                : new[] { "" };

            foreach (var directory in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                foreach (var extension in extensions)
                {
                    var candidate = Path.Combine(directory.Trim().Trim('"'), executable + extension);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            return executable;
        }

        public static ConfigSheetForgeCliInvocation ResolveCoreCli(ProjectConfigSummary summary, string projectRoot, string cliFieldValue)
        {
            summary = summary ?? new ProjectConfigSummary();
            var envName = FirstNonEmpty(summary.CoreCliEnvironmentVariable, "CONFIG_SHEET_FORGE_CLI");
            var envCli = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrWhiteSpace(envCli))
            {
                return ConfigSheetForgeCliInvocation.ForExecutable(envCli, "环境变量 " + envName, "");
            }

            if (!IsDefaultCliName(cliFieldValue))
            {
                return ConfigSheetForgeCliInvocation.ForExecutable(cliFieldValue, "窗口 CLI 字段", "");
            }

            var checkoutEnvName = FirstNonEmpty(summary.SourceCheckoutEnvironmentVariable, "CONFIG_SHEET_FORGE_ROOT");
            var checkoutRoot = Environment.GetEnvironmentVariable(checkoutEnvName);
            if (!string.IsNullOrWhiteSpace(checkoutRoot))
            {
                var relativeProject = FirstNonEmpty(summary.SourceCliProjectRelativePath, Path.Combine("src", "cli", "ConfigSheetForge.Cli"));
                var projectPath = ResolveSourceCliProjectPath(checkoutRoot, relativeProject);
                var dotnet = ConfigSheetForgeCliInvocation.ForExecutable("dotnet", "环境变量 " + checkoutEnvName + " 的源码 checkout", "");
                if (!string.IsNullOrWhiteSpace(projectPath) && dotnet.CanLaunch)
                {
                    dotnet.PrefixArguments.Add("run");
                    dotnet.PrefixArguments.Add("--project");
                    dotnet.PrefixArguments.Add(projectPath);
                    dotnet.PrefixArguments.Add("--");
                    dotnet.SourceDescription = "源码 checkout: " + checkoutEnvName + " -> " + projectPath;
                    return dotnet;
                }

                var reason = string.IsNullOrWhiteSpace(projectPath)
                    ? "已设置 " + checkoutEnvName + "，但找不到 CLI project：" + Path.Combine(checkoutRoot, relativeProject)
                    : "已设置 " + checkoutEnvName + "，但找不到 dotnet 可执行文件。";
                return ConfigSheetForgeCliInvocation.Unresolved(
                    "dotnet",
                    "源码 checkout: " + checkoutEnvName,
                    reason);
            }

            return ConfigSheetForgeCliInvocation.ForExecutable(FirstNonEmpty(cliFieldValue, "config-sheet-forge"), "PATH 或窗口 CLI 字段", "");
        }

        public static string FormatCliLaunchFailure(string command, string reason)
        {
            return FormatCliLaunchFailure(command, reason, null);
        }

        public static string FormatCliLaunchFailure(string command, string reason, ProjectConfigSummary summary)
        {
            summary = summary ?? new ProjectConfigSummary();
            var cliEnv = FirstNonEmpty(summary.CoreCliEnvironmentVariable, "CONFIG_SHEET_FORGE_CLI");
            var rootEnv = FirstNonEmpty(summary.SourceCheckoutEnvironmentVariable, "CONFIG_SHEET_FORGE_ROOT");
            var sourceRelative = FirstNonEmpty(summary.SourceCliProjectRelativePath, Path.Combine("src", "cli", "ConfigSheetForge.Cli"));
            return "无法运行 Config Sheet Forge CLI" + Environment.NewLine +
                   Environment.NewLine +
                   "命令:" + Environment.NewLine +
                   command + Environment.NewLine +
                   Environment.NewLine +
                   "原因:" + Environment.NewLine +
                   HumanizeLaunchReason(reason) + Environment.NewLine +
                   Environment.NewLine +
                   "下一步:" + Environment.NewLine +
                   "1. 设置 " + cliEnv + " 指向 config-sheet-forge 可执行文件。" + Environment.NewLine +
                   "2. 或设置 " + rootEnv + " 指向 config-sheet-forge 源码 checkout，Unity 会运行 dotnet run --project " + sourceRelative + " -- <args>。" + Environment.NewLine +
                   "3. 或安装/发布 CLI，并确认 config-sheet-forge 在 PATH 中。";
        }

        public static string GetConfigPath(string projectRoot)
        {
            return Path.Combine(projectRoot, ".config-sheet-forge", "config.json");
        }

        public static string GetRegistryPath(string projectRoot)
        {
            return Path.Combine(projectRoot, ".config-sheet-forge", "registry.json");
        }

        public static string FindProjectConfigPath(string projectRoot)
        {
            var projectSettings = Path.Combine(projectRoot, "ProjectSettings");
            if (!Directory.Exists(projectSettings))
            {
                return "";
            }

            var candidates = Directory.GetFiles(projectSettings, "*ConfigSheetForge*.json");
            Array.Sort(candidates, StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in candidates)
            {
                return candidate;
            }

            return "";
        }

        public static ProjectConfigSummary LoadProjectConfigSummary(string projectRoot)
        {
            return LoadProjectConfigSummary(projectRoot, "");
        }

        public static ProjectConfigSummary LoadProjectConfigSummary(string projectRoot, string currentGitBranch)
        {
            return ProjectConfigProbe.ProbeFile(FindProjectConfigPath(projectRoot), currentGitBranch);
        }

        public static ConfigSheetForgeCommandSpec CreateProjectLifecycleCommand(
            ProjectConfigSummary summary,
            string projectRoot,
            string operation,
            string requestPath,
            string inputsPath,
            bool dryRun)
        {
            if (summary == null || !summary.HasLifecycleAdapter)
            {
                return new ConfigSheetForgeCommandSpec();
            }

            var command = new ConfigSheetForgeCommandSpec();
            var adapterScript = ResolveProjectPath(projectRoot, summary.AdapterScript);
            if (!string.IsNullOrWhiteSpace(summary.ContractCommand))
            {
                command.Executable = ResolveCommandExecutable(projectRoot, ExpandToken(summary.ContractCommand, projectRoot, summary, operation, requestPath, inputsPath, dryRun));
            }
            else if (!string.IsNullOrWhiteSpace(summary.AdapterInterpreter))
            {
                command.Executable = ExpandToken(summary.AdapterInterpreter, projectRoot, summary, operation, requestPath, inputsPath, dryRun);
                command.Arguments.Add(adapterScript);
            }
            else if (adapterScript.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
            {
                command.Executable = "python";
                command.Arguments.Add(adapterScript);
            }
            else if (adapterScript.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
            {
                command.Executable = "pwsh";
                command.Arguments.Add("-NoProfile");
                command.Arguments.Add("-ExecutionPolicy");
                command.Arguments.Add("Bypass");
                command.Arguments.Add("-File");
                command.Arguments.Add(adapterScript);
            }
            else
            {
                command.Executable = adapterScript;
            }

            if (summary.ContractArguments.Count > 0)
            {
                var hasInputs = false;
                foreach (var arg in summary.ContractArguments)
                {
                    if (string.Equals(arg, "--inputs", StringComparison.OrdinalIgnoreCase) ||
                        arg.IndexOf("{inputs}", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        hasInputs = true;
                    }

                    command.Arguments.Add(ExpandToken(arg, projectRoot, summary, operation, requestPath, inputsPath, dryRun));
                }

                if (!hasInputs)
                {
                    command.Arguments.Add("--inputs");
                    command.Arguments.Add(inputsPath);
                }
            }
            else
            {
                command.Arguments.Add("--project-root");
                command.Arguments.Add(projectRoot);
                command.Arguments.Add("--config");
                command.Arguments.Add(summary.ProjectConfigPath);
                command.Arguments.Add("--operation");
                command.Arguments.Add(operation);
                command.Arguments.Add("--out");
                command.Arguments.Add(requestPath);
                command.Arguments.Add("--inputs");
                command.Arguments.Add(inputsPath);
                if (dryRun)
                {
                    command.Arguments.Add("--dry-run");
                }
            }

            return command;
        }

        public static string ResolveProjectPath(string projectRoot, string value)
        {
            if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value))
            {
                return value ?? "";
            }

            return Path.GetFullPath(Path.Combine(projectRoot, value.Replace('/', Path.DirectorySeparatorChar)));
        }

        public static string ResolveCommandExecutable(string projectRoot, string value)
        {
            if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value))
            {
                return value ?? "";
            }

            return value.IndexOf('/') >= 0 || value.IndexOf('\\') >= 0
                ? ResolveProjectPath(projectRoot, value)
                : value;
        }

        public static string ExpandToken(string value, string projectRoot, ProjectConfigSummary summary, string operation, string requestPath, string inputsPath, bool dryRun)
        {
            summary = summary ?? new ProjectConfigSummary();
            return (value ?? "")
                .Replace("{projectRoot}", projectRoot ?? "")
                .Replace("{projectConfig}", summary.ProjectConfigPath ?? "")
                .Replace("{operation}", operation ?? "")
                .Replace("{request}", requestPath ?? "")
                .Replace("{out}", requestPath ?? "")
                .Replace("{inputs}", inputsPath ?? "")
                .Replace("{dryRun}", dryRun ? "true" : "false");
        }

        private static string ResolveSourceCliProjectPath(string checkoutRoot, string relativeProject)
        {
            if (string.IsNullOrWhiteSpace(checkoutRoot))
            {
                return "";
            }

            var root = checkoutRoot.Trim().Trim('"');
            var combined = Path.IsPathRooted(relativeProject)
                ? relativeProject
                : Path.Combine(root, (relativeProject ?? "").Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(combined))
            {
                return Path.GetFullPath(combined);
            }

            if (Directory.Exists(combined))
            {
                var projects = Directory.GetFiles(combined, "*.csproj");
                Array.Sort(projects, StringComparer.OrdinalIgnoreCase);
                if (projects.Length > 0)
                {
                    return Path.GetFullPath(projects[0]);
                }
            }

            return "";
        }

        private static bool IsDefaultCliName(string value)
        {
            return string.IsNullOrWhiteSpace(value) ||
                   string.Equals(value.Trim(), "config-sheet-forge", StringComparison.OrdinalIgnoreCase);
        }

        private static string HumanizeLaunchReason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return "没有找到 config-sheet-forge CLI。";
            }

            if (reason.IndexOf("ApplicationName=", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("No such file", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("The system cannot find", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("系统找不到", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "没有找到 config-sheet-forge CLI。" + Environment.NewLine + "原始错误：" + reason;
            }

            return reason;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return "";
        }
    }

    public sealed class ConfigSheetForgeCliInvocation
    {
        public string Executable { get; set; } = "";
        public List<string> PrefixArguments { get; private set; } = new List<string>();
        public string SourceDescription { get; set; } = "";
        public string FailureReason { get; set; } = "";

        public bool CanLaunch
        {
            get { return string.IsNullOrWhiteSpace(FailureReason); }
        }

        public static ConfigSheetForgeCliInvocation ForExecutable(string executable, string sourceDescription, string fallbackReason)
        {
            var resolved = ConfigSheetForgeEditorUtility.ResolveExecutable(executable);
            var canLaunch = IsResolvable(resolved);
            return new ConfigSheetForgeCliInvocation
            {
                Executable = resolved,
                SourceDescription = sourceDescription + (string.Equals(resolved, executable, StringComparison.OrdinalIgnoreCase) ? "" : " -> " + resolved),
                FailureReason = canLaunch ? "" : FirstNonEmpty(fallbackReason, "没有找到可执行文件：" + executable)
            };
        }

        public static ConfigSheetForgeCliInvocation Unresolved(string executable, string sourceDescription, string reason)
        {
            return new ConfigSheetForgeCliInvocation
            {
                Executable = executable ?? "",
                SourceDescription = sourceDescription ?? "",
                FailureReason = FirstNonEmpty(reason, "没有找到可执行文件：" + executable)
            };
        }

        public string[] BuildArguments(IEnumerable<string> args)
        {
            var merged = new List<string>();
            merged.AddRange(PrefixArguments);
            foreach (var arg in args)
            {
                merged.Add(arg);
            }

            return merged.ToArray();
        }

        public string ToCommandLine(IEnumerable<string> args)
        {
            return ConfigSheetForgeEditorUtility.BuildCommandLine(Executable, BuildArguments(args));
        }

        private static bool IsResolvable(string executable)
        {
            if (string.IsNullOrWhiteSpace(executable))
            {
                return false;
            }

            if (Path.IsPathRooted(executable) || executable.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) >= 0)
            {
                return File.Exists(executable);
            }

            return !string.Equals(executable, "config-sheet-forge", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(executable, "dotnet", StringComparison.OrdinalIgnoreCase)
                ? true
                : !string.Equals(ConfigSheetForgeEditorUtility.ResolveExecutable(executable), executable, StringComparison.OrdinalIgnoreCase);
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return "";
        }
    }

    public sealed class ConfigSheetForgeCommandSpec
    {
        public string Executable { get; set; } = "";
        public List<string> Arguments { get; private set; } = new List<string>();

        public bool IsValid
        {
            get { return !string.IsNullOrWhiteSpace(Executable); }
        }

        public string ToCommandLine()
        {
            return ConfigSheetForgeEditorUtility.BuildCommandLine(Executable, Arguments);
        }
    }
}

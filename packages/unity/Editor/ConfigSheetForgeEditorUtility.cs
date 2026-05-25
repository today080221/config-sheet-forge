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
            return ProjectConfigProbe.ProbeFile(FindProjectConfigPath(projectRoot));
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

        public static string FormatCliLaunchFailure(string command, string reason)
        {
            return "无法运行 Config Sheet Forge CLI。" + Environment.NewLine +
                   "命令: " + command + Environment.NewLine +
                   "原因: " + reason + Environment.NewLine +
                   "建议: 安装 CLI，或把 CLI 字段设置为可执行文件的绝对路径。";
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

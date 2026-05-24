using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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

            foreach (var candidate in Directory.GetFiles(projectSettings, "*ConfigSheetForge*.json"))
            {
                return candidate;
            }

            return "";
        }

        public static string FormatCliLaunchFailure(string command, string reason)
        {
            return "无法运行 Config Sheet Forge CLI。" + Environment.NewLine +
                   "命令: " + command + Environment.NewLine +
                   "原因: " + reason + Environment.NewLine +
                   "建议: 安装 CLI，或把 CLI 字段设置为可执行文件的绝对路径。";
        }
    }
}

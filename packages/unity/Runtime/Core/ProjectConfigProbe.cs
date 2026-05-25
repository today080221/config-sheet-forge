using System;
using System.Collections.Generic;
using System.IO;

namespace ConfigSheetForge.Core
{
    public sealed class ProjectConfigSummary
    {
        public string ProjectConfigPath { get; set; } = "";
        public bool Exists { get; set; }
        public string SchemaVersion { get; set; } = "";
        public int TableCount { get; set; }
        public string LifecycleApplyMode { get; set; } = "";
        public string GateReportPath { get; set; } = "";
        public string GitBranch { get; set; } = "";
        public string FeishuBranch { get; set; } = "";
        public string Profile { get; set; } = "";
        public string AdapterScript { get; set; } = "";
        public string AdapterInterpreter { get; set; } = "";
        public string ContractCommand { get; set; } = "";
        public string ContractRequestPath { get; set; } = "";
        public List<string> ContractArguments { get; set; } = new List<string>();

        public bool HasLifecycleAdapter
        {
            get
            {
                return !string.IsNullOrWhiteSpace(AdapterScript) ||
                       !string.IsNullOrWhiteSpace(ContractCommand);
            }
        }

        public string BranchProfile
        {
            get
            {
                return FirstNonEmpty(FeishuBranch, Profile);
            }
        }

        public string AdapterDescription
        {
            get
            {
                return FirstNonEmpty(AdapterScript, ContractCommand);
            }
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

    public static class ProjectConfigProbe
    {
        public static ProjectConfigSummary ProbeFile(string path)
        {
            var summary = new ProjectConfigSummary { ProjectConfigPath = path ?? "" };
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return summary;
            }

            return ProbeJson(path, File.ReadAllText(path));
        }

        public static ProjectConfigSummary ProbeJson(string path, string json)
        {
            json = json ?? "";
            var summary = new ProjectConfigSummary
            {
                ProjectConfigPath = path ?? "",
                Exists = true,
                SchemaVersion = FindStringValue(json, "schemaVersion", "schema", "version"),
                TableCount = FindTableCount(json),
                LifecycleApplyMode = FindStringValue(json, "lifecycleApplyMode", "applyMode", "writeMode"),
                GateReportPath = FindStringValue(json, "gateReportPath", "prGateReportPath", "defaultGateReportPath", "reportPath"),
                GitBranch = FindStringValue(json, "gitBranch", "currentGitBranch", "branch"),
                FeishuBranch = FindStringValue(json, "feishuBranch", "larkBranch"),
                Profile = FindStringValue(json, "profile", "feishuProfile", "larkProfile"),
                AdapterScript = FindStringValue(json, "adapterScript", "lifecycleAdapterScript", "contractAdapterScript", "adapterPath"),
                AdapterInterpreter = FindStringValue(json, "adapterInterpreter", "scriptInterpreter", "interpreter"),
                ContractCommand = FindStringValue(json, "contractCommand", "lifecycleContractCommand", "adapterCommand"),
                ContractRequestPath = FindStringValue(json, "contractRequestPath", "requestPath", "contractJsonPath")
            };

            summary.ContractArguments.AddRange(FindStringArray(json, "contractArgs", "adapterArgs", "lifecycleContractArgs"));
            return summary;
        }

        private static string FindStringValue(string json, params string[] keys)
        {
            foreach (var key in keys)
            {
                var valueStart = FindValueStart(json, key);
                if (valueStart < 0)
                {
                    continue;
                }

                valueStart = SkipWhitespace(json, valueStart);
                if (valueStart < json.Length && json[valueStart] == '"')
                {
                    int end;
                    return ParseJsonString(json, valueStart, out end);
                }
            }

            return "";
        }

        private static List<string> FindStringArray(string json, params string[] keys)
        {
            foreach (var key in keys)
            {
                var valueStart = FindValueStart(json, key);
                if (valueStart < 0)
                {
                    continue;
                }

                valueStart = SkipWhitespace(json, valueStart);
                if (valueStart >= json.Length || json[valueStart] != '[')
                {
                    continue;
                }

                var end = FindMatchingBracket(json, valueStart, '[', ']');
                if (end < 0)
                {
                    continue;
                }

                var values = new List<string>();
                for (var i = valueStart + 1; i < end; i++)
                {
                    if (json[i] != '"')
                    {
                        continue;
                    }

                    int stringEnd;
                    values.Add(ParseJsonString(json, i, out stringEnd));
                    i = stringEnd;
                }

                return values;
            }

            return new List<string>();
        }

        private static int FindTableCount(string json)
        {
            foreach (var key in new[] { "tables", "configSheets", "tableMappings", "excelTables", "excelToSoTables" })
            {
                var valueStart = FindValueStart(json, key);
                if (valueStart < 0)
                {
                    continue;
                }

                valueStart = SkipWhitespace(json, valueStart);
                if (valueStart >= json.Length || json[valueStart] != '[')
                {
                    continue;
                }

                var end = FindMatchingBracket(json, valueStart, '[', ']');
                if (end < 0)
                {
                    continue;
                }

                var objectCount = CountTopLevelObjects(json, valueStart, end);
                if (objectCount > 0)
                {
                    return objectCount;
                }

                return CountTopLevelStrings(json, valueStart, end);
            }

            return 0;
        }

        private static int FindValueStart(string json, string key)
        {
            var searchFrom = 0;
            while (searchFrom < json.Length)
            {
                var keyStart = FindQuotedKey(json, key, searchFrom);
                if (keyStart < 0)
                {
                    return -1;
                }

                int keyEnd;
                ParseJsonString(json, keyStart, out keyEnd);
                var colon = SkipWhitespace(json, keyEnd + 1);
                if (colon < json.Length && json[colon] == ':')
                {
                    return colon + 1;
                }

                searchFrom = keyEnd + 1;
            }

            return -1;
        }

        private static int FindQuotedKey(string json, string key, int start)
        {
            for (var i = start; i < json.Length; i++)
            {
                if (json[i] != '"')
                {
                    continue;
                }

                int end;
                var parsed = ParseJsonString(json, i, out end);
                if (string.Equals(parsed, key, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }

                i = end;
            }

            return -1;
        }

        private static string ParseJsonString(string json, int start, out int end)
        {
            var builder = new System.Text.StringBuilder();
            var escaped = false;
            for (var i = start + 1; i < json.Length; i++)
            {
                var c = json[i];
                if (escaped)
                {
                    builder.Append(Unescape(c));
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    end = i;
                    return builder.ToString();
                }

                builder.Append(c);
            }

            end = json.Length - 1;
            return builder.ToString();
        }

        private static char Unescape(char c)
        {
            switch (c)
            {
                case 'n':
                    return '\n';
                case 'r':
                    return '\r';
                case 't':
                    return '\t';
                default:
                    return c;
            }
        }

        private static int SkipWhitespace(string text, int start)
        {
            while (start < text.Length && char.IsWhiteSpace(text[start]))
            {
                start++;
            }

            return start;
        }

        private static int FindMatchingBracket(string text, int start, char open, char close)
        {
            var depth = 0;
            var inString = false;
            var escaped = false;
            for (var i = start; i < text.Length; i++)
            {
                var c = text[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString)
                {
                    continue;
                }

                if (c == open)
                {
                    depth++;
                }
                else if (c == close)
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private static int CountTopLevelObjects(string json, int start, int end)
        {
            return CountTopLevel(json, start, end, '{');
        }

        private static int CountTopLevelStrings(string json, int start, int end)
        {
            return CountTopLevel(json, start, end, '"');
        }

        private static int CountTopLevel(string json, int start, int end, char token)
        {
            var count = 0;
            var depth = 0;
            var inString = false;
            var escaped = false;
            for (var i = start + 1; i < end; i++)
            {
                var c = json[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    if (!inString && depth == 0 && token == '"')
                    {
                        count++;
                    }

                    inString = !inString;
                    continue;
                }

                if (inString)
                {
                    continue;
                }

                if (c == '[')
                {
                    depth++;
                }
                else if (c == ']')
                {
                    depth--;
                }
                else if (c == '{')
                {
                    if (depth == 0 && token == '{')
                    {
                        count++;
                    }

                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                }
            }

            return count;
        }
    }
}

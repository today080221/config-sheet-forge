using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ConfigSheetForge.Unity.Editor
{
    public sealed class ConfigSheetForgeBridgeWindow : EditorWindow
    {
        private const string PackageVersion = "v0.4.29";
        private const string DesktopPathEnv = "CONFIG_SHEET_FORGE_DESKTOP";
        private const string SourceCheckoutEnv = "CONFIG_SHEET_FORGE_ROOT";
        private Vector2 _scroll;
        private string _projectRoot = "";
        private string _projectConfigPath = "";
        private string _recentSummary = "等待操作。";

        [MenuItem("Tools/Config Sheet Forge", false, 1000)]
        public static void OpenStatusWindow()
        {
            var window = GetWindow<ConfigSheetForgeBridgeWindow>("Config Sheet Forge");
            window.titleContent = new GUIContent("Config Sheet Forge");
            window.minSize = new Vector2(520, 420);
            window.RefreshLocalState();
            window.Show();
        }

        [MenuItem("Tools/Config Sheet Forge/打开同步窗口", false, 1001)]
        public static void OpenStatusWindowMenu()
        {
            OpenStatusWindow();
        }

        [MenuItem("Tools/Config Sheet Forge/打开 Desktop 工作台", false, 1002)]
        public static void OpenDesktopMenu()
        {
            OpenStatusWindow();
            var window = GetWindow<ConfigSheetForgeBridgeWindow>();
            window.LaunchDesktop();
        }

        [MenuItem("Tools/Config Sheet Forge/安装或更新 SourceOfTruthCache profile", false, 1003)]
        public static void OpenProfileTool()
        {
            ConfigSheetForgeWindow.OpenSyncCache();
        }

        [MenuItem("Tools/Config Sheet Forge/导入 Unity 配表资产", false, 1004)]
        public static void OpenImportTool()
        {
            ConfigSheetForgeWindow.OpenSyncCache();
        }

        [MenuItem("Tools/Config Sheet Forge/运行 PR 检查", false, 1005)]
        public static void OpenPrGateTool()
        {
            ConfigSheetForgeWindow.OpenPrGate();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Config Sheet Forge");
            RefreshLocalState();
        }

        private void OnGUI()
        {
            if (string.IsNullOrWhiteSpace(_projectRoot))
            {
                RefreshLocalState();
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            GUILayout.Space(8);
            EditorGUILayout.LabelField("配表 Source of Truth", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("默认使用 Desktop 工作台处理同步、合并、审查和 PR gate。Unity 这里只保留必须在 Editor 内完成的桥接动作。", EditorStyles.wordWrappedLabel);
            GUILayout.Space(10);

            DrawStatusCard();
            GUILayout.Space(10);
            DrawPrimaryActions();
            GUILayout.Space(10);
            DrawUnityOnlyActions();
            GUILayout.Space(10);
            DrawRecentResult();
            GUILayout.Space(10);
            DrawLegacyFallback();
            GUILayout.Space(8);
            EditorGUILayout.EndScrollView();
        }

        private void DrawStatusCard()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("当前项目", EditorStyles.boldLabel);
            DrawReadonlyRow("项目目录", string.IsNullOrWhiteSpace(_projectRoot) ? "未识别" : _projectRoot);
            DrawReadonlyRow("项目配置", string.IsNullOrWhiteSpace(_projectConfigPath) ? "未找到 ProjectSettings/*ConfigSheetForge*.json" : _projectConfigPath);
            DrawReadonlyRow("UPM", "dev.config-sheet-forge.unity " + PackageVersion);
            EditorGUILayout.HelpBox("网络读取、导出 xlsx、三方一致性检查和合并预览会在 Desktop/CLI 后台进程里跑，不再放在 Unity IMGUI 主流程里。", MessageType.Info);
            EditorGUILayout.EndVertical();
        }

        private void DrawPrimaryActions()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("推荐入口", EditorStyles.boldLabel);
            if (GUILayout.Button(new GUIContent("打开 Config Sheet Forge Desktop", "Desktop 是官方主工作台：状态、同步、合并、审查、PR gate 都从这里走。"), GUILayout.Height(34)))
            {
                LaunchDesktop();
            }

            EditorGUILayout.LabelField("Desktop 会接收当前 Unity project root；如果未安装 Desktop，可设置 " + DesktopPathEnv + "，或设置 " + SourceCheckoutEnv + " 指向 config-sheet-forge checkout 后用源码模式启动。", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawUnityOnlyActions()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Unity 内动作", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("这些动作只在 Unity Editor 内有意义。它们复用 Legacy 面板里的稳定服务，但不会把完整调试工作台放到默认首页。", EditorStyles.wordWrappedLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("安装/更新 SourceOfTruthCache profile", "只新增或更新 ExcelToSO SourceOfTruthCache profile，不改变 default/local profile。"), GUILayout.Height(30)))
            {
                ConfigSheetForgeWindow.OpenSyncCache();
            }

            if (GUILayout.Button(new GUIContent("导入 Unity 配表资产", "调用 ExcelToSO ImportByProfile(SourceOfTruthCache)，只写 Unity asset。"), GUILayout.Height(30)))
            {
                ConfigSheetForgeWindow.OpenSyncCache();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("运行/读取 PR gate report", "生成或查看 Temp/ConfigSheetForge/pr-gate-report.json。"), GUILayout.Height(28)))
            {
                ConfigSheetForgeWindow.OpenPrGate();
            }

            if (GUILayout.Button(new GUIContent("查看最近结果", "打开 Legacy 输出页和本地 Temp/ConfigSheetForge 结果。"), GUILayout.Height(28)))
            {
                OpenLifecycleDirectory();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawRecentResult()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("最近结果", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(_recentSummary, EditorStyles.wordWrappedLabel);
            var reportPath = Path.Combine(_projectRoot, "Temp", "ConfigSheetForge", "pr-gate-report.json");
            if (File.Exists(reportPath))
            {
                if (GUILayout.Button("打开 PR gate report"))
                {
                    EditorUtility.RevealInFinder(reportPath);
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawLegacyFallback()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Advanced / Legacy Unity Workflow", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("旧 IMGUI 完整工作流仍然保留，用于没有 Desktop、CI 调试或救急 fallback。普通策划日常不建议从这里开始。", MessageType.Warning);
            if (GUILayout.Button("打开 Legacy 完整 Unity 工作台", GUILayout.Height(28)))
            {
                ConfigSheetForgeWindow.OpenStatusWindow();
            }
            EditorGUILayout.EndVertical();
        }

        private void LaunchDesktop()
        {
            RefreshLocalState();
            var desktopPath = Environment.GetEnvironmentVariable(DesktopPathEnv) ?? "";
            try
            {
                if (!string.IsNullOrWhiteSpace(desktopPath) && File.Exists(desktopPath))
                {
                    StartProcess(desktopPath, new[] { _projectRoot }, Path.GetDirectoryName(desktopPath) ?? _projectRoot, visible: true);
                    _recentSummary = "已启动 Desktop：" + desktopPath;
                    return;
                }

                var sourceRoot = Environment.GetEnvironmentVariable(SourceCheckoutEnv) ?? "";
                var desktopPackage = string.IsNullOrWhiteSpace(sourceRoot) ? "" : Path.Combine(sourceRoot, "apps", "desktop", "package.json");
                if (!string.IsNullOrWhiteSpace(sourceRoot) && File.Exists(desktopPackage))
                {
                    var npm = Application.platform == RuntimePlatform.WindowsEditor ? "cmd.exe" : "npm";
                    var args = Application.platform == RuntimePlatform.WindowsEditor
                        ? new[] { "/C", "npm", "run", "tauri", "--", "dev" }
                        : new[] { "run", "tauri", "--", "dev" };
                    StartProcess(npm, args, Path.GetDirectoryName(desktopPackage) ?? sourceRoot, visible: false);
                    _recentSummary = "已用源码模式启动 Desktop。首次启动可能需要编译 Tauri。";
                    return;
                }

                var message = "没有找到 Config Sheet Forge Desktop。\n\n下一步：\n1. 安装 Desktop 后设置 " + DesktopPathEnv + " 指向可执行文件。\n2. 或设置 " + SourceCheckoutEnv + " 指向 config-sheet-forge checkout，用源码模式启动 apps/desktop。\n\n当前项目：" + _projectRoot;
                _recentSummary = message;
                EditorUtility.DisplayDialog("需要安装 Desktop", message, "知道了");
            }
            catch (Exception ex)
            {
                _recentSummary = "无法启动 Desktop：" + ex.Message;
                EditorUtility.DisplayDialog("无法启动 Desktop", _recentSummary, "知道了");
            }
        }

        private static void StartProcess(string executable, string[] args, string workingDirectory, bool visible)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Directory.GetCurrentDirectory() : workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = !visible
            };

            foreach (var arg in args ?? Array.Empty<string>())
            {
                startInfo.ArgumentList.Add(arg);
            }

            Process.Start(startInfo);
        }

        private void OpenLifecycleDirectory()
        {
            var dir = Path.Combine(_projectRoot, "Temp", "ConfigSheetForge");
            if (Directory.Exists(dir))
            {
                EditorUtility.RevealInFinder(dir);
                return;
            }

            _recentSummary = "还没有找到 Temp/ConfigSheetForge。请先在 Desktop 或 Legacy 中运行一次流程。";
        }

        private void RefreshLocalState()
        {
            _projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Directory.GetCurrentDirectory();
            _projectConfigPath = ConfigSheetForgeEditorUtility.FindProjectConfigPath(_projectRoot);
        }

        private static void DrawReadonlyRow(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(86));
            EditorGUILayout.SelectableLabel(value ?? "", EditorStyles.wordWrappedLabel, GUILayout.MinHeight(18));
            EditorGUILayout.EndHorizontal();
        }
    }
}

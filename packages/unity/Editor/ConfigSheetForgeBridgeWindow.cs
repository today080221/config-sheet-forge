using System;
using System.IO.Compression;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace ConfigSheetForge.Unity.Editor
{
    public sealed class ConfigSheetForgeBridgeWindow : EditorWindow
    {
        internal static string PackageVersion => ConfigSheetForgePackageVersion.TagVersion;
        private const string DesktopPathEnv = "CONFIG_SHEET_FORGE_DESKTOP";
        private const string SourceCheckoutEnv = "CONFIG_SHEET_FORGE_ROOT";
        private const string DesktopInstallPathPrefKey = "ConfigSheetForge.Desktop.InstallPath";
        private const string DesktopInstallVersionPrefKey = "ConfigSheetForge.Desktop.InstallVersion";
        private const string DesktopInstallShaPrefKey = "ConfigSheetForge.Desktop.InstallSha256";
        private const string DesktopArtifactNamePrefix = "config-sheet-forge-desktop-windows-x64-";
        internal const string DesktopExecutableName = "ConfigSheetForgeDesktop.exe";
        private const string ReleaseBaseUrl = "https://github.com/today080221/config-sheet-forge/releases/download/";
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
        private Vector2 _scroll;
        private string _projectRoot = "";
        private string _projectConfigPath = "";
        private string _recentSummary = "等待操作。";
        private DesktopDiscovery _desktopDiscovery = DesktopDiscovery.Empty;
        private DesktopInstallJob _desktopInstallJob;
        private bool _showDeveloperDesktopOptions;
        private string _bridgeSessionDirectory = "";

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
            RefreshDesktopDiscovery();
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            ProcessBridgeSessionCommands();

            if (_desktopInstallJob == null)
            {
                return;
            }

            if (!_desktopInstallJob.IsCompleted)
            {
                Repaint();
                return;
            }

            var result = _desktopInstallJob.Result;
            _desktopInstallJob = null;
            if (result.Success)
            {
                EditorPrefs.SetString(DesktopInstallPathPrefKey, result.ExecutablePath);
                EditorPrefs.SetString(DesktopInstallVersionPrefKey, PackageVersion);
                EditorPrefs.SetString(DesktopInstallShaPrefKey, result.Sha256);
                _recentSummary = "Desktop 已安装：" + result.ExecutablePath;
                RefreshDesktopDiscovery();
                LaunchDesktop();
            }
            else
            {
                _recentSummary = result.Message;
            }

            Repaint();
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
            DrawReadonlyRow("Desktop", _desktopDiscovery.StatusText);
            DrawReadonlyRow("Desktop 版本", _desktopDiscovery.VersionText);
            EditorGUILayout.HelpBox("网络读取、导出 xlsx、三方一致性检查和合并预览会在 Desktop/CLI 后台进程里跑，不再放在 Unity IMGUI 主流程里。", MessageType.Info);
            EditorGUILayout.EndVertical();
        }

        private void DrawPrimaryActions()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("推荐入口", EditorStyles.boldLabel);
            var canOpen = _desktopDiscovery.HasRunnableDesktop || _desktopDiscovery.HasSourceMode;
            using (new EditorGUI.DisabledScope(_desktopInstallJob != null))
            {
                if (GUILayout.Button(new GUIContent("打开 Config Sheet Forge Desktop", "Desktop 是官方主工作台：状态、同步、合并、审查、PR gate 都从这里走。"), GUILayout.Height(34)))
                {
                    LaunchDesktop();
                }
            }

            if (_desktopInstallJob != null)
            {
                EditorGUILayout.HelpBox(_desktopInstallJob.StatusText, MessageType.Info);
            }
            else if (!canOpen || _desktopDiscovery.NeedsUpdate)
            {
                var label = _desktopDiscovery.HasInstalledDesktop ? "更新 Desktop 到 " + PackageVersion : "安装 Desktop " + PackageVersion;
                EditorGUILayout.HelpBox(_desktopDiscovery.InstallPrompt, MessageType.Warning);
                if (GUILayout.Button(new GUIContent(label, "下载当前 UPM 版本对应的 Desktop artifact，校验 sha256 后安装到本机用户目录。"), GUILayout.Height(30)))
                {
                    ConfirmAndStartDesktopInstall();
                }
                EditorGUILayout.LabelField("安装不会改仓库文件、ProjectSettings、Packages 或旧 Excel/。需要联网下载 GitHub Release artifact。", EditorStyles.wordWrappedMiniLabel);
            }
            else if (_desktopDiscovery.IsNewerThanPackage)
            {
                EditorGUILayout.HelpBox("已安装 Desktop 版本高于当前 UPM，可能不兼容。普通用户建议更新 UPM 或安装同版本 Desktop；程序视图可继续打开。", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.LabelField("Desktop 会接收当前 Unity project root；推荐 UPM 与 Desktop 使用同一个 tag。", EditorStyles.wordWrappedMiniLabel);
            }

            _showDeveloperDesktopOptions = EditorGUILayout.Foldout(_showDeveloperDesktopOptions, "高级 / 开发者启动方式");
            if (_showDeveloperDesktopOptions)
            {
                EditorGUILayout.HelpBox("开发者可以设置 " + DesktopPathEnv + " 指向本地 Desktop exe，或设置 " + SourceCheckoutEnv + " 指向 config-sheet-forge checkout 后用源码模式启动。普通用户优先使用上面的安装按钮。", MessageType.Info);
                DrawReadonlyRow("ENV Desktop", FirstNonEmpty(Environment.GetEnvironmentVariable(DesktopPathEnv), "未设置"));
                DrawReadonlyRow("ENV Source", FirstNonEmpty(Environment.GetEnvironmentVariable(SourceCheckoutEnv), "未设置"));
            }
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
            RefreshDesktopDiscovery();
            try
            {
                if (_desktopDiscovery.HasRunnableDesktop)
                {
                    var desktopPath = _desktopDiscovery.ExecutablePath;
                    if (LooksLikeDevDesktopBuild(desktopPath))
                    {
                        var devBuildMessage = BuildDevDesktopBuildMessage(desktopPath);
                        _recentSummary = devBuildMessage;
                        EditorUtility.DisplayDialog("Desktop 需要升级", devBuildMessage, "知道了");
                        return;
                    }

                    EnsureBridgeSessionDirectory();
                    StartProcess(desktopPath, new[] { _projectRoot, "--bridge-session", _bridgeSessionDirectory }, Path.GetDirectoryName(desktopPath) ?? _projectRoot, visible: true);
                    _recentSummary = "已启动 Desktop：" + desktopPath;
                    return;
                }

                if (_desktopDiscovery.HasSourceMode)
                {
                    var desktopPackage = Path.Combine(_desktopDiscovery.SourceRoot, "apps", "desktop", "package.json");
                    var npm = Application.platform == RuntimePlatform.WindowsEditor ? "cmd.exe" : "npm";
                    EnsureBridgeSessionDirectory();
                    var args = Application.platform == RuntimePlatform.WindowsEditor
                        ? new[] { "/C", "npm", "run", "tauri", "--", "dev", "--", _projectRoot, "--bridge-session", _bridgeSessionDirectory }
                        : new[] { "run", "tauri", "--", "dev", "--", _projectRoot, "--bridge-session", _bridgeSessionDirectory };
                    StartProcess(npm, args, Path.GetDirectoryName(desktopPackage) ?? _desktopDiscovery.SourceRoot, visible: false);
                    _recentSummary = "已用源码模式启动 Desktop。首次启动可能需要编译 Tauri。";
                    return;
                }

                var message = "未安装 Config Sheet Forge Desktop。请点击“安装 Desktop " + PackageVersion + "”。\n\n如果无法联网，可手动下载：\n" + BuildReleaseArtifactUrl() + "\n\n当前项目：" + _projectRoot;
                _recentSummary = message;
                EditorUtility.DisplayDialog("需要安装 Desktop", message, "知道了");
            }
            catch (Exception ex)
            {
                _recentSummary = "无法启动 Desktop：" + HumanizeDesktopLaunchException(ex);
                EditorUtility.DisplayDialog("无法启动 Desktop", _recentSummary, "知道了");
            }
        }

        private void ConfirmAndStartDesktopInstall()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                _recentSummary = "当前自动安装只支持 Windows x64。macOS / Linux 请先使用开发者源码模式或手动安装。";
                return;
            }

            var installDir = BuildDesktopInstallDirectory(PackageVersion);
            var downloadUrl = BuildReleaseArtifactUrl();
            var checksumUrl = downloadUrl + ".sha256";
            var message =
                "将安装 Config Sheet Forge Desktop " + PackageVersion + "。\n\n" +
                "下载源：\n" + downloadUrl + "\n\n" +
                "校验：下载后会读取 sha256 文件并校验 zip。\n\n" +
                "安装位置：\n" + installDir + "\n\n" +
                "不会写仓库文件，不改 ProjectSettings，不改 Packages，不写旧 Excel/。";
            if (!EditorUtility.DisplayDialog("安装/更新 Desktop", message, "下载并安装", "取消"))
            {
                return;
            }

            _desktopInstallJob = DesktopInstallJob.Start(PackageVersion, downloadUrl, checksumUrl, installDir);
            _recentSummary = "正在下载 Desktop " + PackageVersion + "。";
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
            if (string.IsNullOrWhiteSpace(_projectRoot) || !Directory.Exists(_projectRoot))
            {
                _recentSummary = "还没有识别到项目目录，暂时不能打开结果目录。";
                return;
            }

            var dir = Path.Combine(_projectRoot, "Temp", "ConfigSheetForge", "desktop");
            try
            {
                Directory.CreateDirectory(dir);
                EditorUtility.RevealInFinder(dir);
                _recentSummary = "已打开 Desktop 结果目录：" + dir;
            }
            catch (Exception ex)
            {
                _recentSummary = "无法创建或打开 Desktop 结果目录：" + ex.Message;
            }
        }

        private void EnsureBridgeSessionDirectory()
        {
            if (!string.IsNullOrWhiteSpace(_bridgeSessionDirectory) && Directory.Exists(_bridgeSessionDirectory))
            {
                return;
            }

            var root = Directory.GetParent(Application.dataPath)?.FullName ?? Directory.GetCurrentDirectory();
            _bridgeSessionDirectory = Path.Combine(root, "Library", "ConfigSheetForge", "DesktopBridge", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_bridgeSessionDirectory, "commands"));
            File.WriteAllText(Path.Combine(_bridgeSessionDirectory, "session.json"), "{\"projectRoot\":\"" + EscapeJson(root) + "\",\"version\":\"" + PackageVersion + "\"}", Utf8NoBom);
        }

        private void ProcessBridgeSessionCommands()
        {
            if (string.IsNullOrWhiteSpace(_bridgeSessionDirectory))
            {
                return;
            }

            var commands = Path.Combine(_bridgeSessionDirectory, "commands");
            if (!Directory.Exists(commands))
            {
                return;
            }

            foreach (var file in Directory.GetFiles(commands, "*.json").OrderBy(path => path))
            {
                try
                {
                    var text = File.ReadAllText(file);
                    var operation = ExtractJsonString(text, "operation");
                    var processed = Path.ChangeExtension(file, ".processed.json");
                    if (string.Equals(operation, "import-assets", StringComparison.OrdinalIgnoreCase))
                    {
                        ConfigSheetForgeWindow.OpenSyncCache();
                        _recentSummary = "Desktop 请求导入 Unity 配表资产。已打开 Unity 导入面板，请确认 SourceOfTruthCache profile 和 cache 状态后执行。";
                    }
                    else if (string.Equals(operation, "install-profile", StringComparison.OrdinalIgnoreCase))
                    {
                        ConfigSheetForgeWindow.OpenSyncCache();
                        _recentSummary = "Desktop 请求安装/更新 SourceOfTruthCache profile。已打开 Unity profile 面板。";
                    }
                    else if (string.Equals(operation, "read-pr-gate", StringComparison.OrdinalIgnoreCase))
                    {
                        OpenPrGateTool();
                        _recentSummary = "Desktop 请求读取 PR gate report。";
                    }
                    else
                    {
                        _recentSummary = "Desktop 发来了未知 Unity bridge 命令：" + operation;
                    }

                    if (File.Exists(processed))
                    {
                        File.Delete(processed);
                    }

                    File.Move(file, processed);
                }
                catch (Exception ex)
                {
                    _recentSummary = "处理 Desktop bridge 命令失败：" + ex.Message;
                }
            }
        }

        private static string ExtractJsonString(string json, string key)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
            {
                return "";
            }

            var marker = "\"" + key + "\"";
            var index = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return "";
            }

            var colon = json.IndexOf(':', index + marker.Length);
            if (colon < 0)
            {
                return "";
            }

            var quote = json.IndexOf('"', colon + 1);
            if (quote < 0)
            {
                return "";
            }

            var end = quote + 1;
            while (end < json.Length)
            {
                if (json[end] == '"' && json[end - 1] != '\\')
                {
                    break;
                }

                end++;
            }

            return end < json.Length ? json.Substring(quote + 1, end - quote - 1).Replace("\\\"", "\"") : "";
        }

        private static string EscapeJson(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private void RefreshLocalState()
        {
            _projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Directory.GetCurrentDirectory();
            _projectConfigPath = ConfigSheetForgeEditorUtility.FindProjectConfigPath(_projectRoot);
        }

        private void RefreshDesktopDiscovery()
        {
            _desktopDiscovery = DiscoverDesktop();
        }

        private static DesktopDiscovery DiscoverDesktop()
        {
            var discovery = DesktopDiscovery.Empty;
            var installedPath = EditorPrefs.GetString(DesktopInstallPathPrefKey, "");
            var installedVersion = EditorPrefs.GetString(DesktopInstallVersionPrefKey, "");
            if (!string.IsNullOrWhiteSpace(installedPath) && File.Exists(installedPath))
            {
                discovery.ExecutablePath = installedPath;
                discovery.InstalledVersion = FirstNonEmpty(ReadInstalledDesktopVersion(installedPath), installedVersion);
                discovery.Source = "已安装";
                return discovery.WithVersionStatus(PackageVersion);
            }

            var envPath = Environment.GetEnvironmentVariable(DesktopPathEnv) ?? "";
            if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
            {
                discovery.ExecutablePath = envPath;
                discovery.InstalledVersion = ReadInstalledDesktopVersion(envPath);
                discovery.Source = DesktopPathEnv;
                return discovery.WithVersionStatus(PackageVersion);
            }

            var sourceRoot = Environment.GetEnvironmentVariable(SourceCheckoutEnv) ?? "";
            var desktopPackage = string.IsNullOrWhiteSpace(sourceRoot) ? "" : Path.Combine(sourceRoot, "apps", "desktop", "package.json");
            if (!string.IsNullOrWhiteSpace(sourceRoot) && File.Exists(desktopPackage))
            {
                discovery.SourceRoot = sourceRoot;
                discovery.Source = "源码模式";
                return discovery.WithVersionStatus(PackageVersion);
            }

            return discovery.WithVersionStatus(PackageVersion);
        }

        private static string BuildReleaseArtifactUrl()
        {
            return ReleaseBaseUrl + PackageVersion + "/" + DesktopArtifactNamePrefix + PackageVersion + ".zip";
        }

        private static string BuildDesktopInstallDirectory(string version)
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local");
            }

            return Path.Combine(localAppData, "ConfigSheetForge", "Desktop", version);
        }

        private static string ReadInstalledDesktopVersion(string executablePath)
        {
            try
            {
                var versionPath = Path.Combine(Path.GetDirectoryName(executablePath) ?? "", "VERSION.txt");
                return File.Exists(versionPath) ? File.ReadAllText(versionPath).Trim() : "";
            }
            catch
            {
                return "";
            }
        }

        private static string HumanizeDesktopLaunchException(Exception ex)
        {
            var message = ex == null ? "" : ex.Message;
            if (message.IndexOf("not find", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("找不到", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "文件不存在或路径失效。请重新安装 Desktop，或在高级模式检查 " + DesktopPathEnv + "。";
            }

            if (message.IndexOf("access", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("拒绝", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "系统拒绝启动 Desktop，可能被杀软、权限或企业策略拦截。请检查安装目录权限后重试。原始原因：" + message;
            }

            if (message.IndexOf("npm", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("cargo", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "源码模式需要 npm/cargo/Tauri 环境。普通用户建议点击安装 Desktop。原始原因：" + message;
            }

            if (message.IndexOf("port", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("端口", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("address", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Desktop 启动端口可能被占用。请关闭旧的 Desktop/开发服务器后重试。原始原因：" + message;
            }

            return message;
        }

        internal static bool LooksLikeDevDesktopBuild(string executablePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                {
                    return false;
                }

                var bytes = File.ReadAllBytes(executablePath);
                var text = Encoding.ASCII.GetString(bytes);
                return text.IndexOf("http://127.0.0.1:1420", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("127.0.0.1:1420", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("http://localhost:1420", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("localhost:1420", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        internal static string BuildDevDesktopBuildMessage(string executablePath)
        {
            return "Desktop release 包疑似开发构建，请升级 config-sheet-forge Desktop。\n\n"
                + "检测到 Desktop 可执行文件仍指向 127.0.0.1:1420 / localhost:1420。生产版 Desktop 不需要 Node、Vite 或 CONFIG_SHEET_FORGE_ROOT。\n\n"
                + "请点击“安装 Desktop " + PackageVersion + "”重新安装，或手动下载同版本 GitHub Release。\n\n"
                + "当前文件：" + executablePath;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return "";
        }

        private static void DrawReadonlyRow(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(86));
            EditorGUILayout.SelectableLabel(value ?? "", EditorStyles.wordWrappedLabel, GUILayout.MinHeight(18));
            EditorGUILayout.EndHorizontal();
        }
    }

    internal sealed class DesktopDiscovery
    {
        public static DesktopDiscovery Empty { get { return new DesktopDiscovery(); } }

        public string ExecutablePath = "";
        public string SourceRoot = "";
        public string InstalledVersion = "";
        public string Source = "";
        public bool NeedsUpdate;
        public bool IsNewerThanPackage;

        public bool HasRunnableDesktop { get { return !string.IsNullOrWhiteSpace(ExecutablePath) && File.Exists(ExecutablePath); } }
        public bool HasInstalledDesktop { get { return string.Equals(Source, "已安装", StringComparison.OrdinalIgnoreCase) && HasRunnableDesktop; } }
        public bool HasSourceMode { get { return !string.IsNullOrWhiteSpace(SourceRoot) && File.Exists(Path.Combine(SourceRoot, "apps", "desktop", "package.json")); } }

        public string StatusText
        {
            get
            {
                if (HasRunnableDesktop)
                {
                    return Source + "：" + ExecutablePath;
                }

                if (HasSourceMode)
                {
                    return "源码模式：" + SourceRoot;
                }

                return "未安装，点击安装 Desktop";
            }
        }

        public string VersionText
        {
            get
            {
                if (HasSourceMode && !HasRunnableDesktop)
                {
                    return "源码模式，版本随 checkout";
                }

                if (string.IsNullOrWhiteSpace(InstalledVersion))
                {
                    return HasRunnableDesktop ? "未知版本，建议安装同 tag Desktop" : "未安装";
                }

                if (NeedsUpdate)
                {
                    return InstalledVersion + "，低于 UPM " + ConfigSheetForgeBridgeWindow.PackageVersion;
                }

                if (IsNewerThanPackage)
                {
                    return InstalledVersion + "，高于 UPM " + ConfigSheetForgeBridgeWindow.PackageVersion;
                }

                return InstalledVersion + "，与 UPM 匹配";
            }
        }

        public string InstallPrompt
        {
            get
            {
                if (NeedsUpdate)
                {
                    return "已安装 Desktop 版本低于当前 UPM。建议更新到同 tag，避免 Desktop/Unity contract 不一致。";
                }

                return "未安装 Desktop。点击安装后会下载当前 UPM 版本对应的 Windows x64 portable zip，并校验 sha256。";
            }
        }

        public DesktopDiscovery WithVersionStatus(string packageVersion)
        {
            if (!string.IsNullOrWhiteSpace(InstalledVersion))
            {
                var installed = NormalizeVersion(InstalledVersion);
                var package = NormalizeVersion(packageVersion);
                var comparison = CompareVersions(installed, package);
                NeedsUpdate = comparison < 0;
                IsNewerThanPackage = comparison > 0;
            }

            return this;
        }

        private static string NormalizeVersion(string version)
        {
            return (version ?? "").Trim().TrimStart('v', 'V');
        }

        private static int CompareVersions(string a, string b)
        {
            Version left;
            Version right;
            if (Version.TryParse(a, out left) && Version.TryParse(b, out right))
            {
                return left.CompareTo(right);
            }

            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class DesktopInstallJob
    {
        private Task<DesktopInstallResult> _task;

        private DesktopInstallJob(Task<DesktopInstallResult> task)
        {
            _task = task;
        }

        public bool IsCompleted { get { return _task.IsCompleted; } }
        public DesktopInstallResult Result
        {
            get
            {
                if (!_task.IsCompleted)
                {
                    return new DesktopInstallResult { Success = false, Message = StatusText };
                }

                return _task.Result;
            }
        }

        public string StatusText { get; private set; } = "正在准备下载 Desktop...";

        public static DesktopInstallJob Start(string version, string downloadUrl, string checksumUrl, string installDir)
        {
            var job = new DesktopInstallJob(null);
            job._task = Task.Run(() => job.Run(version, downloadUrl, checksumUrl, installDir));
            return job;
        }

        private DesktopInstallResult Run(string version, string downloadUrl, string checksumUrl, string installDir)
        {
            try
            {
                var tempRoot = Path.Combine(Path.GetTempPath(), "ConfigSheetForge", "DesktopInstall", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempRoot);
                var zipPath = Path.Combine(tempRoot, Path.GetFileName(new Uri(downloadUrl).LocalPath));
                var checksumPath = zipPath + ".sha256";

                StatusText = "正在下载 checksum...";
                DownloadFile(checksumUrl, checksumPath);
                var expectedSha = ParseSha256(File.ReadAllText(checksumPath));
                if (string.IsNullOrWhiteSpace(expectedSha))
                {
                    return DesktopInstallResult.Fail("sha256 文件格式不正确。请手动下载并校验：" + checksumUrl);
                }

                StatusText = "正在下载 Desktop portable zip...";
                DownloadFile(downloadUrl, zipPath);
                StatusText = "正在校验 sha256...";
                var actualSha = ComputeSha256(zipPath);
                if (!string.Equals(expectedSha, actualSha, StringComparison.OrdinalIgnoreCase))
                {
                    return DesktopInstallResult.Fail("sha256 校验失败，已停止安装。\n期望：" + expectedSha + "\n实际：" + actualSha + "\n请检查网络或手动下载：" + downloadUrl);
                }

                StatusText = "正在解压 Desktop...";
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrWhiteSpace(localAppData))
                {
                    localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local");
                }

                var installRoot = Path.GetFullPath(Path.Combine(localAppData, "ConfigSheetForge", "Desktop"));
                var resolvedInstallDir = Path.GetFullPath(installDir);
                if (!IsSubPathOf(resolvedInstallDir, installRoot))
                {
                    return DesktopInstallResult.Fail("安装路径不在用户本地目录下，已阻断：" + resolvedInstallDir);
                }

                if (Directory.Exists(resolvedInstallDir))
                {
                    Directory.Delete(resolvedInstallDir, true);
                }

                Directory.CreateDirectory(resolvedInstallDir);
                ZipFile.ExtractToDirectory(zipPath, resolvedInstallDir);
                var executable = Directory.GetFiles(resolvedInstallDir, ConfigSheetForgeBridgeWindow.DesktopExecutableName, SearchOption.AllDirectories).FirstOrDefault()
                                 ?? Directory.GetFiles(resolvedInstallDir, "*.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
                {
                    return DesktopInstallResult.Fail("安装包里没有找到 Desktop exe。请手动下载检查：" + downloadUrl);
                }

                if (ConfigSheetForgeBridgeWindow.LooksLikeDevDesktopBuild(executable))
                {
                    return DesktopInstallResult.Fail(ConfigSheetForgeBridgeWindow.BuildDevDesktopBuildMessage(executable));
                }

                File.WriteAllText(Path.Combine(Path.GetDirectoryName(executable) ?? resolvedInstallDir, "VERSION.txt"), version, new UTF8Encoding(false));
                return new DesktopInstallResult
                {
                    Success = true,
                    ExecutablePath = executable,
                    Sha256 = actualSha,
                    Message = "Desktop 安装完成。"
                };
            }
            catch (WebException ex)
            {
                return DesktopInstallResult.Fail("下载 Desktop 失败。请检查网络，或手动下载：\n" + downloadUrl + "\n\n原因：" + ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return DesktopInstallResult.Fail("写入安装目录失败，可能被权限或杀软拦截。请检查 %LOCALAPPDATA%/ConfigSheetForge/Desktop。\n原因：" + ex.Message);
            }
            catch (Exception ex)
            {
                return DesktopInstallResult.Fail("安装 Desktop 失败：" + ex.Message + "\n可手动下载：" + downloadUrl);
            }
        }

        private static void DownloadFile(string url, string path)
        {
            using (var client = new WebClient())
            {
                client.Headers.Add("User-Agent", "ConfigSheetForge-Unity");
                client.DownloadFile(url, path);
            }
        }

        private static string ParseSha256(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            var tokens = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return tokens.FirstOrDefault(token => token.Length == 64 && token.All(IsHex)) ?? "";
        }

        private static bool IsHex(char ch)
        {
            return (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');
        }

        private static string ComputeSha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            }
        }

        private static bool IsSubPathOf(string path, string root)
        {
            var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class DesktopInstallResult
    {
        public bool Success;
        public string ExecutablePath = "";
        public string Sha256 = "";
        public string Message = "";

        public static DesktopInstallResult Fail(string message)
        {
            return new DesktopInstallResult { Success = false, Message = message ?? "" };
        }
    }
}

using System;
using System.Reflection;

namespace ConfigSheetForge.Unity.Editor
{
    internal static class ConfigSheetForgePackageVersion
    {
        private static string _cachedSemVer;

        internal static string SemVer
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_cachedSemVer))
                {
                    _cachedSemVer = ResolveSemVer();
                }

                return _cachedSemVer;
            }
        }

        internal static string TagVersion
        {
            get
            {
                var semver = SemVer;
                return semver.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? semver : "v" + semver;
            }
        }

        private static string ResolveSemVer()
        {
            try
            {
                var packageInfoType = typeof(UnityEditor.PackageManager.PackageInfo);
                var findForAssembly = packageInfoType.GetMethod("FindForAssembly", BindingFlags.Public | BindingFlags.Static);
                var packageInfo = findForAssembly?.Invoke(null, new object[] { typeof(ConfigSheetForgePackageVersion).Assembly });
                var version = ReadPackageInfoVersion(packageInfo);
                if (!string.IsNullOrWhiteSpace(version))
                {
                    return NormalizeSemVer(version);
                }

                var findForAssetPath = packageInfoType.GetMethod("FindForAssetPath", BindingFlags.Public | BindingFlags.Static);
                packageInfo = findForAssetPath?.Invoke(null, new object[] { "Packages/dev.config-sheet-forge.unity/package.json" });
                version = ReadPackageInfoVersion(packageInfo);
                if (!string.IsNullOrWhiteSpace(version))
                {
                    return NormalizeSemVer(version);
                }
            }
            catch
            {
                // Unity PackageManager metadata can be unavailable in compile smoke contexts.
            }

            try
            {
                var packageJson = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>("Packages/dev.config-sheet-forge.unity/package.json");
                var version = ExtractPackageJsonVersion(packageJson != null ? packageJson.text : "");
                if (!string.IsNullOrWhiteSpace(version))
                {
                    return NormalizeSemVer(version);
                }
            }
            catch
            {
                // AssetDatabase may not be available while the editor domain is compiling.
            }

            return "0.0.0-dev";
        }

        private static string NormalizeSemVer(string version)
        {
            return (version ?? "").Trim().TrimStart('v');
        }

        private static string ReadPackageInfoVersion(object packageInfo)
        {
            if (packageInfo == null)
            {
                return "";
            }

            var property = packageInfo.GetType().GetProperty("version", BindingFlags.Public | BindingFlags.Instance);
            return property?.GetValue(packageInfo) as string ?? "";
        }

        private static string ExtractPackageJsonVersion(string json)
        {
            const string property = "\"version\"";
            var index = (json ?? "").IndexOf(property, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return "";
            }

            var colon = json.IndexOf(':', index + property.Length);
            if (colon < 0)
            {
                return "";
            }

            var firstQuote = json.IndexOf('"', colon + 1);
            if (firstQuote < 0)
            {
                return "";
            }

            var secondQuote = json.IndexOf('"', firstQuote + 1);
            return secondQuote > firstQuote ? json.Substring(firstQuote + 1, secondQuote - firstQuote - 1) : "";
        }
    }
}

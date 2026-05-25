using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConfigSheetForge.Core
{
    public sealed class BranchWorkspaceContract
    {
        public string Mode { get; set; } = "git-branch-to-feishu-branch-profile";
        public string RootWikiToken { get; set; } = "";
        public string RootWikiUrl { get; set; } = "";
        public string RootWikiTitle { get; set; } = "项目配置表";
        public string GitBranch { get; set; } = "";
        public string FeishuBranch { get; set; } = "";
        public string Profile { get; set; } = "";
        public string MainGitBranch { get; set; } = "main";
        public string MainFeishuBranch { get; set; } = "main";
        public string ProfileNameTemplate { get; set; } = "{gitBranch}";
        public string BranchNodeTitleTemplate { get; set; } = "branch-{slug}";
        public string MainNodeTitle { get; set; } = "main";
        public bool CreateIfMissing { get; set; } = true;
        public bool RequireOneToOneBinding { get; set; } = true;
        public string BindingRegistryTable { get; set; } = "BranchBindings";
        public string OwnerRole { get; set; } = "";
        public string CreatedBy { get; set; } = "";
        public string ExistingWikiNodeToken { get; set; } = "";
        public string ExistingWikiNodeUrl { get; set; } = "";
    }

    public sealed class BranchWorkspaceResolution
    {
        public string Mode { get; set; } = "";
        public string RootWikiToken { get; set; } = "";
        public string RootWikiUrl { get; set; } = "";
        public string RootWikiTitle { get; set; } = "";
        public string GitBranch { get; set; } = "";
        public string FeishuBranch { get; set; } = "";
        public string Profile { get; set; } = "";
        public string Slug { get; set; } = "";
        public string NodeTitle { get; set; } = "";
        public string WikiNodeToken { get; set; } = "";
        public string WikiNodeUrl { get; set; } = "";
        public string Status { get; set; } = "";
        public string BindingRegistryTable { get; set; } = "BranchBindings";
        public string OwnerRole { get; set; } = "";
        public string CreatedBy { get; set; } = "";
        public bool CreateIfMissing { get; set; }
        public bool RequireOneToOneBinding { get; set; }
        public bool IsMain { get; set; }
    }

    public interface IBranchWorkspacePlatform
    {
        Task<BranchWorkspaceResolution> EnsureBranchWorkspaceAsync(BranchWorkspaceContract workspace, BranchWorkspaceResolution planned, CancellationToken cancellationToken);
        Task<LifecycleActionResult> UpsertBranchBindingAsync(RegistryContract registry, BranchWorkspaceResolution resolution, CancellationToken cancellationToken);
    }

    public static class BranchWorkspaceResolver
    {
        public static BranchWorkspaceResolution Resolve(LifecycleContractRequest request)
        {
            request = request ?? new LifecycleContractRequest();
            if (request.Git == null)
            {
                request.Git = new ContractGitSpec();
            }

            if (request.SeedFromLocalXlsx == null)
            {
                request.SeedFromLocalXlsx = new SeedFromLocalXlsxContract();
            }

            if (request.Table == null)
            {
                request.Table = new ContractTableSpec();
            }

            var workspace = NormalizeContract(request);
            var gitBranch = FirstNonEmpty(workspace.GitBranch, request.Git.Branch, "main");
            var mainGitBranch = FirstNonEmpty(workspace.MainGitBranch, "main");
            var isMain = string.Equals(gitBranch, mainGitBranch, StringComparison.OrdinalIgnoreCase);
            var profileTemplate = FirstNonEmpty(workspace.ProfileNameTemplate, "{gitBranch}");
            var profile = FirstNonEmpty(workspace.Profile, request.Git.Profile, ApplyTemplate(profileTemplate, gitBranch, "", "", ""));
            var feishuBranch = FirstNonEmpty(workspace.FeishuBranch, request.Git.FeishuBranch, isMain ? FirstNonEmpty(workspace.MainFeishuBranch, "main") : profile, gitBranch);
            if (isMain && string.IsNullOrWhiteSpace(workspace.Profile) && string.IsNullOrWhiteSpace(request.Git.Profile))
            {
                profile = FirstNonEmpty(workspace.MainFeishuBranch, feishuBranch, "main");
            }

            var slugBasis = isMain ? FirstNonEmpty(workspace.MainFeishuBranch, profile, feishuBranch, gitBranch, "main") : FirstNonEmpty(profile, feishuBranch, gitBranch);
            var slug = isMain ? "main" : Slugify(slugBasis);
            var nodeTitle = isMain
                ? FirstNonEmpty(workspace.MainNodeTitle, workspace.MainFeishuBranch, "main")
                : ApplyTemplate(FirstNonEmpty(workspace.BranchNodeTitleTemplate, "branch-{slug}"), gitBranch, feishuBranch, profile, slug);

            var resolution = new BranchWorkspaceResolution
            {
                Mode = FirstNonEmpty(workspace.Mode, "git-branch-to-feishu-branch-profile"),
                RootWikiToken = FirstNonEmpty(workspace.RootWikiToken, request.SeedFromLocalXlsx.WikiRootToken, request.Table.WikiRootToken),
                RootWikiUrl = workspace.RootWikiUrl,
                RootWikiTitle = FirstNonEmpty(workspace.RootWikiTitle, request.SeedFromLocalXlsx.WikiParentTitle, "项目配置表"),
                GitBranch = gitBranch,
                FeishuBranch = feishuBranch,
                Profile = profile,
                Slug = slug,
                NodeTitle = nodeTitle,
                WikiNodeToken = FirstNonEmpty(workspace.ExistingWikiNodeToken),
                WikiNodeUrl = FirstNonEmpty(workspace.ExistingWikiNodeUrl),
                Status = "planned",
                BindingRegistryTable = FirstNonEmpty(workspace.BindingRegistryTable, "BranchBindings"),
                OwnerRole = workspace.OwnerRole,
                CreatedBy = workspace.CreatedBy,
                CreateIfMissing = workspace.CreateIfMissing,
                RequireOneToOneBinding = workspace.RequireOneToOneBinding,
                IsMain = isMain
            };

            ApplyExistingBinding(request.BranchBindings, resolution);
            return resolution;
        }

        public static BranchWorkspaceContract NormalizeContract(LifecycleContractRequest request)
        {
            request = request ?? new LifecycleContractRequest();
            if (request.Git == null)
            {
                request.Git = new ContractGitSpec();
            }

            if (request.SeedFromLocalXlsx == null)
            {
                request.SeedFromLocalXlsx = new SeedFromLocalXlsxContract();
            }

            if (request.Table == null)
            {
                request.Table = new ContractTableSpec();
            }

            var configured = request.BranchWorkspace ?? new BranchWorkspaceContract();
            return new BranchWorkspaceContract
            {
                Mode = FirstNonEmpty(configured.Mode, "git-branch-to-feishu-branch-profile"),
                RootWikiToken = FirstNonEmpty(configured.RootWikiToken, request.SeedFromLocalXlsx.WikiRootToken, request.Table.WikiRootToken),
                RootWikiUrl = configured.RootWikiUrl,
                RootWikiTitle = FirstNonEmpty(configured.RootWikiTitle, request.SeedFromLocalXlsx.WikiParentTitle, "项目配置表"),
                GitBranch = FirstNonEmpty(configured.GitBranch, request.Git.Branch, "main"),
                FeishuBranch = FirstNonEmpty(configured.FeishuBranch, request.Git.FeishuBranch),
                Profile = FirstNonEmpty(configured.Profile, request.Git.Profile),
                MainGitBranch = FirstNonEmpty(configured.MainGitBranch, "main"),
                MainFeishuBranch = FirstNonEmpty(configured.MainFeishuBranch, "main"),
                ProfileNameTemplate = FirstNonEmpty(configured.ProfileNameTemplate, "{gitBranch}"),
                BranchNodeTitleTemplate = FirstNonEmpty(configured.BranchNodeTitleTemplate, "branch-{slug}"),
                MainNodeTitle = FirstNonEmpty(configured.MainNodeTitle, "main"),
                CreateIfMissing = configured.CreateIfMissing,
                RequireOneToOneBinding = configured.RequireOneToOneBinding,
                BindingRegistryTable = FirstNonEmpty(configured.BindingRegistryTable, "BranchBindings"),
                OwnerRole = configured.OwnerRole,
                CreatedBy = configured.CreatedBy,
                ExistingWikiNodeToken = configured.ExistingWikiNodeToken,
                ExistingWikiNodeUrl = configured.ExistingWikiNodeUrl
            };
        }

        public static void ValidateOneToOne(LifecycleContractRequest request, BranchWorkspaceResolution resolution, LifecycleContractResult result)
        {
            if (request == null || resolution == null || result == null || !resolution.RequireOneToOneBinding)
            {
                return;
            }

            var bindings = request.BranchBindings ?? new List<BranchBindingContract>();
            var effectiveProfile = FirstNonEmpty(resolution.Profile, resolution.FeishuBranch);
            var duplicateBindings = bindings
                .Where(b => !string.IsNullOrWhiteSpace(b.GitBranch) && !string.IsNullOrWhiteSpace(FirstNonEmpty(b.Profile, b.FeishuBranch)))
                .GroupBy(b => b.GitBranch + "\n" + FirstNonEmpty(b.Profile, b.FeishuBranch), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .ToList();
            foreach (var duplicate in duplicateBindings)
            {
                var first = duplicate.First();
                var profile = FirstNonEmpty(first.Profile, first.FeishuBranch);
                var recordIds = duplicate.Select(b => FirstNonEmpty(b.RecordId, "(无 record_id)")).ToList();
                result.AddFailure("BranchBindings 中 GitBranch “" + first.GitBranch + "” + Profile “" + profile + "” 存在 " + duplicate.Count().ToString(CultureInfo.InvariantCulture) + " 条重复记录（record_id: " + string.Join(", ", recordIds) + "）。请先运行 registry-migrate --dry-run 查看 cleanup/migrate 计划，确认后清理重复行。");
            }

            var branchProfiles = bindings
                .Where(b => string.Equals(b.GitBranch, resolution.GitBranch, StringComparison.OrdinalIgnoreCase))
                .Select(b => FirstNonEmpty(b.Profile, b.FeishuBranch))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (branchProfiles.Count > 1 || (branchProfiles.Count == 1 && !string.Equals(branchProfiles[0], effectiveProfile, StringComparison.OrdinalIgnoreCase)))
            {
                result.AddFailure("当前 Git 分支 “" + resolution.GitBranch + "” 已绑定到其他 Feishu profile（" + string.Join(", ", branchProfiles) + "）。请在 BranchBindings 中保留唯一绑定，或切换到对应 git 分支后重试。");
            }

            var profileBranches = bindings
                .Where(b => string.Equals(FirstNonEmpty(b.Profile, b.FeishuBranch), effectiveProfile, StringComparison.OrdinalIgnoreCase))
                .Select(b => b.GitBranch)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (profileBranches.Count > 1 || (profileBranches.Count == 1 && !string.Equals(profileBranches[0], resolution.GitBranch, StringComparison.OrdinalIgnoreCase)))
            {
                result.AddFailure("Feishu profile “" + effectiveProfile + "” 已被其他 Git 分支使用（" + string.Join(", ", profileBranches) + "）。请给当前分支换一个独立 profile，或清理 BranchBindings 后重试。");
            }
        }

        public static LifecycleActionResult BuildAction(BranchWorkspaceResolution resolution, string status, string message)
        {
            var action = new LifecycleActionResult
            {
                Action = "branch_workspace.resolve",
                Status = status,
                Message = message
            };
            action.Details["gitBranch"] = resolution.GitBranch;
            action.Details["feishuBranch"] = resolution.FeishuBranch;
            action.Details["profile"] = resolution.Profile;
            action.Details["slug"] = resolution.Slug;
            action.Details["rootWikiToken"] = resolution.RootWikiToken;
            action.Details["rootWikiUrl"] = resolution.RootWikiUrl;
            action.Details["rootWikiTitle"] = resolution.RootWikiTitle;
            action.Details["nodeTitle"] = resolution.NodeTitle;
            action.Details["wikiNodeToken"] = resolution.WikiNodeToken;
            action.Details["wikiNodeUrl"] = resolution.WikiNodeUrl;
            action.Details["createIfMissing"] = resolution.CreateIfMissing.ToString().ToLowerInvariant();
            action.Details["bindingRegistryTable"] = resolution.BindingRegistryTable;
            return action;
        }

        public static string Slugify(string value)
        {
            value = (value ?? "").Trim();
            if (value.Length == 0)
            {
                return "branch";
            }

            var builder = new StringBuilder();
            var pendingDash = false;
            foreach (var c in value.ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    if (pendingDash && builder.Length > 0)
                    {
                        builder.Append('-');
                    }

                    builder.Append(c);
                    pendingDash = false;
                }
                else
                {
                    pendingDash = builder.Length > 0;
                }

                if (builder.Length >= 96)
                {
                    break;
                }
            }

            var slug = builder.ToString().Trim('-');
            return string.IsNullOrWhiteSpace(slug) ? "branch" : slug;
        }

        private static void ApplyExistingBinding(IEnumerable<BranchBindingContract> bindings, BranchWorkspaceResolution resolution)
        {
            if (bindings == null)
            {
                return;
            }

            var effectiveProfile = FirstNonEmpty(resolution.Profile, resolution.FeishuBranch);
            var match = bindings.FirstOrDefault(b =>
                string.Equals(b.GitBranch, resolution.GitBranch, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(FirstNonEmpty(b.Profile, b.FeishuBranch), effectiveProfile, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                return;
            }

            resolution.WikiNodeToken = FirstNonEmpty(resolution.WikiNodeToken, match.WikiNodeToken);
            resolution.WikiNodeUrl = FirstNonEmpty(resolution.WikiNodeUrl, match.WikiNodeUrl);
            resolution.NodeTitle = FirstNonEmpty(match.NodeTitle, resolution.NodeTitle);
            resolution.Status = FirstNonEmpty(match.Status, "existing");
            resolution.OwnerRole = FirstNonEmpty(resolution.OwnerRole, match.OwnerRole);
            resolution.CreatedBy = FirstNonEmpty(resolution.CreatedBy, match.CreatedBy);
        }

        private static string ApplyTemplate(string template, string gitBranch, string feishuBranch, string profile, string slug)
        {
            return (template ?? "")
                .Replace("{gitBranch}", gitBranch ?? "")
                .Replace("{feishuBranch}", feishuBranch ?? "")
                .Replace("{profile}", profile ?? "")
                .Replace("{slug}", slug ?? "");
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
}

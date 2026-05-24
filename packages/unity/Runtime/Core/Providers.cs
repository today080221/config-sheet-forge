using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ConfigSheetForge.Core
{
    public sealed class ProviderContext
    {
        public string WorkspaceRoot { get; set; } = "";
        public Dictionary<string, string> Settings { get; set; } = new Dictionary<string, string>();
    }

    public sealed class ProviderDoctorFinding
    {
        public FindingSeverity Severity { get; set; }
        public string Code { get; set; } = "";
        public string Message { get; set; } = "";
        public Dictionary<string, string> Details { get; set; } = new Dictionary<string, string>();
    }

    public sealed class ProviderRootCandidate
    {
        public string ProviderId { get; set; } = "";
        public string Title { get; set; } = "";
        public string ObjectType { get; set; } = "";
        public string TokenOrId { get; set; } = "";
        public string Url { get; set; } = "";
        public string Reason { get; set; } = "";
        public Dictionary<string, string> Details { get; set; } = new Dictionary<string, string>();
    }

    public sealed class ProviderExportRequest
    {
        public string RootTokenOrUrl { get; set; } = "";
        public string SpreadsheetTokenOrUrl { get; set; } = "";
        public string TableId { get; set; } = "";
        public string SheetId { get; set; } = "";
        public string Range { get; set; } = "";
        public string CacheDirectory { get; set; } = "";
        public bool PreferXlsx { get; set; } = true;
        public Dictionary<string, string> Settings { get; set; } = new Dictionary<string, string>();
    }

    public sealed class ProviderExportResult
    {
        public string CachePath { get; set; } = "";
        public string ProviderRevision { get; set; } = "";
        public string SemanticHash { get; set; } = "";
        public WorkbookDocument Workbook { get; set; }
        public List<ProviderDoctorFinding> Findings { get; set; } = new List<ProviderDoctorFinding>();
    }

    public interface IWorkbookProvider
    {
        string Id { get; }
        Task<IList<ProviderDoctorFinding>> DoctorAsync(ProviderContext context, CancellationToken cancellationToken);
        Task<IList<ProviderRootCandidate>> DiscoverRootsAsync(ProviderContext context, string query, CancellationToken cancellationToken);
        Task<ProviderExportResult> ExportAsync(ProviderContext context, ProviderExportRequest request, CancellationToken cancellationToken);
    }
}

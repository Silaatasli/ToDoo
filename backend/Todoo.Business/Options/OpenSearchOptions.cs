namespace Todoo.Business.Options;

public class OpenSearchOptions
{
    public const string SectionName = "OpenSearch";

    public string Uri { get; set; } = "http://localhost:9200";

    public string SprintAuditIndex { get; set; } = "todoo-sprint-audit";

    /// <summary>false ise yazma/okuma no-op; uygulama ayağa kalkmaya devam eder.</summary>
    public bool Enabled { get; set; } = true;
}

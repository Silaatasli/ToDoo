namespace Todoo.Business.Options;

public class LuceneSearchOptions
{
    public const string SectionName = "LuceneSearch";

    public string IndexPath { get; set; } = string.Empty;
}

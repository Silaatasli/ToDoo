using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenSearch.Client;
using Todoo.Business.Abstract;
using Todoo.Business.Models.Sprints;
using Todoo.Business.Options;

namespace Todoo.Business.Concrete;

public class SprintAuditSearchService : ISprintAuditSearchService
{
    private readonly IOpenSearchClient _client;
    private readonly OpenSearchOptions _options;
    private readonly ILogger<SprintAuditSearchService> _logger;
    private int _indexReady;

    public SprintAuditSearchService(
        IOpenSearchClient client,
        IOptions<OpenSearchOptions> options,
        ILogger<SprintAuditSearchService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task IndexAsync(SprintAuditWriteRequest entry, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return;
        }

        try
        {
            await EnsureIndexAsync(cancellationToken);
            var document = new SprintAuditDocument
            {
                Id = Guid.NewGuid().ToString("N"),
                TeamId = entry.TeamId,
                BoardId = entry.BoardId,
                SprintId = entry.SprintId,
                SprintName = entry.SprintName,
                TaskId = entry.TaskId,
                UserId = entry.UserId,
                UserEmail = entry.UserEmail,
                ActionType = entry.ActionType,
                OldValue = entry.OldValue,
                NewValue = entry.NewValue,
                CreatedDate = entry.CreatedDate.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(entry.CreatedDate, DateTimeKind.Utc)
                    : entry.CreatedDate.ToUniversalTime()
            };

            var response = await _client.IndexAsync(
                document,
                descriptor => descriptor.Index(_options.SprintAuditIndex).Id(document.Id),
                cancellationToken);

            if (!response.IsValid)
            {
                _logger.LogWarning(
                    "OpenSearch sprint audit yazilamadi: {Error}",
                    response.ServerError?.ToString() ?? response.OriginalException?.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenSearch sprint audit yazma basarisiz (SQL kaydi korunur).");
        }
    }

    public async Task<IReadOnlyList<SprintAuditEntryDto>> SearchBySprintAsync(
        int sprintId,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return [];
        }

        try
        {
            await EnsureIndexAsync(cancellationToken);
            var response = await _client.SearchAsync<SprintAuditDocument>(
                search => search
                    .Index(_options.SprintAuditIndex)
                    .Size(Math.Clamp(take, 1, 500))
                    .Query(q => q.Term(t => t.Field(f => f.SprintId).Value(sprintId)))
                    .Sort(s => s.Descending(f => f.CreatedDate)),
                cancellationToken);

            if (!response.IsValid)
            {
                _logger.LogWarning(
                    "OpenSearch sprint audit okunamadi: {Error}",
                    response.ServerError?.ToString() ?? response.OriginalException?.Message);
                return [];
            }

            return response.Documents
                .Select(doc => new SprintAuditEntryDto
                {
                    Id = doc.Id,
                    TeamId = doc.TeamId,
                    BoardId = doc.BoardId,
                    SprintId = doc.SprintId,
                    SprintName = doc.SprintName,
                    TaskId = doc.TaskId,
                    UserId = doc.UserId,
                    UserEmail = doc.UserEmail,
                    ActionType = doc.ActionType,
                    OldValue = doc.OldValue,
                    NewValue = doc.NewValue,
                    CreatedDate = doc.CreatedDate,
                    Source = "opensearch"
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenSearch sprint audit arama basarisiz.");
            return [];
        }
    }

    private async Task EnsureIndexAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _indexReady, 1, 1) == 1)
        {
            return;
        }

        var exists = await _client.Indices.ExistsAsync(_options.SprintAuditIndex, ct: cancellationToken);
        if (exists.Exists)
        {
            Interlocked.Exchange(ref _indexReady, 1);
            return;
        }

        var create = await _client.Indices.CreateAsync(
            _options.SprintAuditIndex,
            c => c.Map<SprintAuditDocument>(m => m.AutoMap()),
            cancellationToken);

        if (!create.IsValid && create.ServerError?.Error?.Type != "resource_already_exists_exception")
        {
            Interlocked.Exchange(ref _indexReady, 0);
            throw create.OriginalException
                ?? new InvalidOperationException(create.ServerError?.ToString() ?? "Index olusturulamadi.");
        }

        Interlocked.Exchange(ref _indexReady, 1);
    }

    private sealed class SprintAuditDocument
    {
        public string Id { get; set; } = string.Empty;
        public int TeamId { get; set; }
        public int BoardId { get; set; }
        public int SprintId { get; set; }
        public string SprintName { get; set; } = string.Empty;
        public int? TaskId { get; set; }
        public int UserId { get; set; }
        public string? UserEmail { get; set; }
        public string ActionType { get; set; } = string.Empty;
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
        public DateTime CreatedDate { get; set; }
    }
}

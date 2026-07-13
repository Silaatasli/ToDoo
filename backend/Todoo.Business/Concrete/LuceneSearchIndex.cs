using System.Globalization;
using System.Text;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Extensions.Options;
using Todoo.Business.Abstract;
using Todoo.Business.Helpers;
using Todoo.Business.Models;
using Todoo.Business.Options;
using Todoo.Entities.Entities;
using Directory = Lucene.Net.Store.Directory;

namespace Todoo.Business.Concrete;

public sealed class LuceneSearchIndex : ILuceneSearchIndex, IDisposable
{
    private const LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;
    private const string TypeTeam = "team";
    private const string TypeTask = "task";
    private const string TypePerson = "person";

    private readonly object _sync = new();
    private readonly StandardAnalyzer _analyzer;
    private readonly Directory _directory;
    private readonly IndexWriter _writer;
    private readonly SearcherManager _searcherManager;
    private bool _disposed;

    public LuceneSearchIndex(IOptions<LuceneSearchOptions> options)
    {
        var indexPath = options.Value.IndexPath;
        if (string.IsNullOrWhiteSpace(indexPath))
        {
            throw new InvalidOperationException("Lucene index yolu yapilandirilmadi.");
        }

        System.IO.Directory.CreateDirectory(indexPath);

        _analyzer = new StandardAnalyzer(AppLuceneVersion);
        _directory = FSDirectory.Open(indexPath);
        var config = new IndexWriterConfig(AppLuceneVersion, _analyzer)
        {
            OpenMode = OpenMode.CREATE_OR_APPEND
        };
        _writer = new IndexWriter(_directory, config);
        _searcherManager = new SearcherManager(_writer, applyAllDeletes: true, new SearcherFactory());
    }

    public void IndexTeam(Team team)
    {
        if (team.IsPersonal)
        {
            RemoveTeam(team.Id);
            return;
        }

        Upsert(DocId(TypeTeam, team.Id), BuildTeamDocument(team));
    }

    public void IndexTask(TaskItem task, string teamName, string boardColumnTitle)
    {
        Upsert(DocId(TypeTask, task.Id), BuildTaskDocument(task, teamName, boardColumnTitle));
    }

    public void IndexPerson(User user, IEnumerable<int> nonPersonalTeamIds)
    {
        var teamIds = nonPersonalTeamIds.Distinct().ToList();
        if (teamIds.Count == 0)
        {
            RemovePerson(user.Id);
            return;
        }

        Upsert(DocId(TypePerson, user.Id), BuildPersonDocument(user, teamIds));
    }

    public void RemoveTeam(int teamId) => DeleteByDocId(DocId(TypeTeam, teamId));

    public void RemoveTask(int taskId) => DeleteByDocId(DocId(TypeTask, taskId));

    public void RemovePerson(int userId) => DeleteByDocId(DocId(TypePerson, userId));

    public void RemoveTasksForTeam(int teamId)
    {
        lock (_sync)
        {
            _writer.DeleteDocuments(BuildTypeAndTeamQuery(TypeTask, teamId));
            CommitAndRefresh();
        }
    }

    public GlobalSearchResultDto Search(string query, IReadOnlyCollection<int> visibleTeamIds, int maxResultsPerSection)
    {
        var term = NormalizeSearchText(query);
        if (term.Length < 3 || visibleTeamIds.Count == 0)
        {
            return new GlobalSearchResultDto();
        }

        lock (_sync)
        {
            _searcherManager.MaybeRefreshBlocking();
            var searcher = _searcherManager.Acquire();
            try
            {
                var textQuery = BuildTextQuery(term);

                var teams = SearchTeams(searcher, textQuery, visibleTeamIds, maxResultsPerSection);
                var tasks = SearchTasks(searcher, textQuery, visibleTeamIds, maxResultsPerSection);
                var people = SearchPeople(searcher, textQuery, visibleTeamIds, maxResultsPerSection);

                return new GlobalSearchResultDto
                {
                    Teams = teams,
                    Tasks = tasks,
                    People = people
                };
            }
            finally
            {
                _searcherManager.Release(searcher);
            }
        }
    }

    public Task RebuildAsync(
        IEnumerable<Team> teams,
        IEnumerable<(TaskItem Task, string TeamName, string BoardColumnTitle)> tasks,
        IEnumerable<(User User, IReadOnlyCollection<int> TeamIds)> people,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _writer.DeleteAll();

            foreach (var team in teams)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (team.IsPersonal)
                {
                    continue;
                }

                _writer.AddDocument(BuildTeamDocument(team));
            }

            foreach (var (task, teamName, boardColumnTitle) in tasks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _writer.AddDocument(BuildTaskDocument(task, teamName, boardColumnTitle));
            }

            foreach (var (user, teamIds) in people)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (teamIds.Count == 0)
                {
                    continue;
                }

                _writer.AddDocument(BuildPersonDocument(user, teamIds));
            }

            CommitAndRefresh();
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _searcherManager.Dispose();
        _writer.Dispose();
        _directory.Dispose();
        _analyzer.Dispose();
    }

    private void Upsert(string docId, Document document)
    {
        lock (_sync)
        {
            _writer.UpdateDocument(new Term("docId", docId), document);
            CommitAndRefresh();
        }
    }

    private void DeleteByDocId(string docId)
    {
        lock (_sync)
        {
            _writer.DeleteDocuments(new Term("docId", docId));
            CommitAndRefresh();
        }
    }

    private void CommitAndRefresh()
    {
        _writer.Commit();
        _searcherManager.MaybeRefreshBlocking();
    }

    private static Document BuildTeamDocument(Team team)
    {
        var name = team.Name ?? string.Empty;
        return new Document
        {
            new StringField("docId", DocId(TypeTeam, team.Id), Field.Store.YES),
            new StringField("type", TypeTeam, Field.Store.YES),
            new Int32Field("id", team.Id, Field.Store.YES),
            new StringField("name", name, Field.Store.YES),
            new StringField("searchText", NormalizeSearchText(name), Field.Store.NO),
            new TextField("content", name, Field.Store.NO)
        };
    }

    private static Document BuildTaskDocument(TaskItem task, string teamName, string boardColumnTitle)
    {
        var title = task.Title ?? string.Empty;
        var description = task.Description ?? string.Empty;
        var content = $"{title} {description}".Trim();
        return new Document
        {
            new StringField("docId", DocId(TypeTask, task.Id), Field.Store.YES),
            new StringField("type", TypeTask, Field.Store.YES),
            new Int32Field("id", task.Id, Field.Store.YES),
            new Int32Field("teamId", task.TeamId, Field.Store.YES),
            new StringField("title", title, Field.Store.YES),
            new StringField("teamName", teamName ?? string.Empty, Field.Store.YES),
            new StringField("boardColumnTitle", boardColumnTitle ?? string.Empty, Field.Store.YES),
            new Int64Field("createdTicks", task.CreatedDate.Ticks, Field.Store.YES),
            new StringField("searchText", NormalizeSearchText(content), Field.Store.NO),
            new TextField("content", content, Field.Store.NO)
        };
    }

    private static Document BuildPersonDocument(User user, IReadOnlyCollection<int> teamIds)
    {
        var displayName = UserDisplayNameHelper.Format(user);
        var firstName = user.FirstName ?? string.Empty;
        var lastName = user.LastName ?? string.Empty;
        var fullName = $"{firstName} {lastName}".Trim();
        var content = $"{firstName} {lastName} {fullName} {displayName} {user.Email}".Trim();

        var doc = new Document
        {
            new StringField("docId", DocId(TypePerson, user.Id), Field.Store.YES),
            new StringField("type", TypePerson, Field.Store.YES),
            new Int32Field("id", user.Id, Field.Store.YES),
            new StringField("email", user.Email ?? string.Empty, Field.Store.YES),
            new StringField("displayName", displayName, Field.Store.YES),
            new StringField("hasProfilePhoto", (!string.IsNullOrWhiteSpace(user.ProfilePhotoObjectKey)).ToString(), Field.Store.YES),
            new StringField("searchText", NormalizeSearchText(content), Field.Store.NO),
            new TextField("content", content, Field.Store.NO)
        };

        foreach (var teamId in teamIds.Distinct())
        {
            doc.Add(new Int32Field("teamId", teamId, Field.Store.YES));
        }

        return doc;
    }

    private static Query BuildTextQuery(string term)
    {
        var query = new BooleanQuery { MinimumNumberShouldMatch = 1 };

        // Exact / substring (eski Contains davranisi)
        query.Add(new WildcardQuery(new Term("searchText", $"*{EscapeWildcard(term)}*")), Occur.SHOULD);

        var tokens = term.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var token in tokens)
        {
            if (token.Length < 3)
            {
                continue;
            }

            var maxEdits = token.Length <= 5 ? 1 : 2;
            query.Add(new FuzzyQuery(new Term("content", token), maxEdits), Occur.SHOULD);
            query.Add(new PrefixQuery(new Term("content", token)), Occur.SHOULD);
        }

        return query;
    }

    private static List<GlobalSearchTeamDto> SearchTeams(
        IndexSearcher searcher,
        Query textQuery,
        IReadOnlyCollection<int> visibleTeamIds,
        int maxResults)
    {
        var filter = new BooleanQuery
        {
            { new TermQuery(new Term("type", TypeTeam)), Occur.MUST },
            { textQuery, Occur.MUST },
            { BuildTeamIdFilter(visibleTeamIds, idField: "id"), Occur.MUST }
        };

        var hits = searcher.Search(filter, maxResults * 4).ScoreDocs;
        var culture = StringComparer.Create(new CultureInfo("tr-TR"), ignoreCase: true);

        return hits
            .Select(hit => searcher.Doc(hit.Doc))
            .Select(doc => new GlobalSearchTeamDto
            {
                Id = doc.GetField("id").GetInt32Value() ?? 0,
                Name = doc.Get("name") ?? string.Empty
            })
            .OrderBy(team => team.Name, culture)
            .Take(maxResults)
            .ToList();
    }

    private static List<GlobalSearchTaskDto> SearchTasks(
        IndexSearcher searcher,
        Query textQuery,
        IReadOnlyCollection<int> visibleTeamIds,
        int maxResults)
    {
        var filter = new BooleanQuery
        {
            { new TermQuery(new Term("type", TypeTask)), Occur.MUST },
            { textQuery, Occur.MUST },
            { BuildTeamIdFilter(visibleTeamIds, idField: "teamId"), Occur.MUST }
        };

        var hits = searcher.Search(filter, maxResults * 4).ScoreDocs;

        return hits
            .Select(hit => searcher.Doc(hit.Doc))
            .Select(doc => new
            {
                Dto = new GlobalSearchTaskDto
                {
                    Id = doc.GetField("id").GetInt32Value() ?? 0,
                    Title = doc.Get("title") ?? string.Empty,
                    TeamId = doc.GetField("teamId").GetInt32Value() ?? 0,
                    TeamName = doc.Get("teamName") ?? string.Empty,
                    BoardColumnTitle = doc.Get("boardColumnTitle") ?? string.Empty
                },
                CreatedTicks = doc.GetField("createdTicks").GetInt64Value() ?? 0
            })
            .OrderByDescending(item => item.CreatedTicks)
            .Take(maxResults)
            .Select(item => item.Dto)
            .ToList();
    }

    private static List<GlobalSearchPersonDto> SearchPeople(
        IndexSearcher searcher,
        Query textQuery,
        IReadOnlyCollection<int> visibleTeamIds,
        int maxResults)
    {
        var filter = new BooleanQuery
        {
            { new TermQuery(new Term("type", TypePerson)), Occur.MUST },
            { textQuery, Occur.MUST },
            { BuildTeamIdFilter(visibleTeamIds, idField: "teamId"), Occur.MUST }
        };

        var hits = searcher.Search(filter, maxResults * 4).ScoreDocs;
        var culture = StringComparer.Create(new CultureInfo("tr-TR"), ignoreCase: true);

        return hits
            .Select(hit => searcher.Doc(hit.Doc))
            .Select(doc => new GlobalSearchPersonDto
            {
                Id = doc.GetField("id").GetInt32Value() ?? 0,
                Email = doc.Get("email") ?? string.Empty,
                DisplayName = doc.Get("displayName") ?? string.Empty,
                HasProfilePhoto = bool.TryParse(doc.Get("hasProfilePhoto"), out var hasPhoto) && hasPhoto
            })
            .GroupBy(person => person.Id)
            .Select(group => group.First())
            .OrderBy(person => person.DisplayName, culture)
            .Take(maxResults)
            .ToList();
    }

    private static BooleanQuery BuildTeamIdFilter(IReadOnlyCollection<int> teamIds, string idField)
    {
        var query = new BooleanQuery();
        foreach (var teamId in teamIds)
        {
            query.Add(NumericRangeQuery.NewInt32Range(idField, teamId, teamId, true, true), Occur.SHOULD);
        }

        query.MinimumNumberShouldMatch = 1;
        return query;
    }

    private static BooleanQuery BuildTypeAndTeamQuery(string type, int teamId) => new()
    {
        { new TermQuery(new Term("type", type)), Occur.MUST },
        { NumericRangeQuery.NewInt32Range("teamId", teamId, teamId, true, true), Occur.MUST }
    };

    private static string DocId(string type, int id) => $"{type}:{id}";

    private static string NormalizeSearchText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().ToLowerInvariant();
    }

    private static string EscapeWildcard(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch is '*' or '?' or '\\')
            {
                builder.Append('\\');
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }
}

using Todoo.Business.Abstract;
using Todoo.Business.Models;
using Todoo.DataAccess.UnitOfWork;

namespace Todoo.Business.Concrete;

public class SearchService : ISearchService
{
    private const int MaxResultsPerSection = 8;

    private readonly IUnitOfWork _unitOfWork;
    private readonly ILuceneSearchIndex _searchIndex;

    public SearchService(IUnitOfWork unitOfWork, ILuceneSearchIndex searchIndex)
    {
        _unitOfWork = unitOfWork;
        _searchIndex = searchIndex;
    }

    public async Task<ServiceResult<GlobalSearchResultDto>> SearchAsync(string query, int userId)
    {
        var term = query?.Trim() ?? string.Empty;
        if (term.Length < 3)
        {
            return ServiceResult<GlobalSearchResultDto>.Fail("En az 3 karakter girin.");
        }

        var memberships = await _unitOfWork.TeamMembers.GetAllAsync();
        var myTeamIds = memberships
            .Where(member => member.UserId == userId)
            .Select(member => member.TeamId)
            .ToHashSet();

        var visibleTeamIds = (await _unitOfWork.Teams.GetAllAsync())
            .Where(team => myTeamIds.Contains(team.Id) && !team.IsPersonal)
            .Select(team => team.Id)
            .ToList();

        var result = _searchIndex.Search(term, visibleTeamIds, MaxResultsPerSection);
        return ServiceResult<GlobalSearchResultDto>.Ok(result);
    }
}

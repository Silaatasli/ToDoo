using Todoo.Business.Models;

namespace Todoo.Business.Abstract;

public interface ISearchService
{
    Task<ServiceResult<GlobalSearchResultDto>> SearchAsync(string query, int userId);
}

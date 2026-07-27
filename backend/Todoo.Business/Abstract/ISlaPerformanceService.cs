using Todoo.Business.Models;
using Todoo.Business.Models.Reports;

namespace Todoo.Business.Abstract;

public interface ISlaPerformanceService
{
    /// <summary>Kullanicinin bu takimdaki SLA performansi.</summary>
    Task<ServiceResult<SlaPerformanceDto>> GetMyPerformanceAsync(int teamId, int userId);

    /// <summary>Takim lideri: uyelerin bu takimdaki SLA ozeti.</summary>
    Task<ServiceResult<TeamSlaMembersDto>> GetTeamMembersPerformanceAsync(int teamId, int requesterUserId);
}

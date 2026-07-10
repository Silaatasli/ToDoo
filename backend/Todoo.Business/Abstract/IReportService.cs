using Todoo.Business.Models;
using Todoo.Business.Models.Reports;

namespace Todoo.Business.Abstract;

public interface IReportService
{
    Task<ServiceResult<TaskReportDto>> GetTaskReportByTeamIdAsync(int teamId, int userId);
}

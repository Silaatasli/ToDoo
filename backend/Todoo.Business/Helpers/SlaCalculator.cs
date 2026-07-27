using Todoo.Entities.Entities;
using Todoo.Entities.Enums;

namespace Todoo.Business.Helpers;

public enum SlaStatus
{
    NotTracked,
    OnTrack,
    Met,
    Breached
}

public static class SlaCalculator
{
    public static int GetPriorityWeight(Priority priority) => priority switch
    {
        Priority.Low => 1,
        Priority.Medium => 2,
        Priority.High => 3,
        Priority.Critical => 4,
        _ => 1
    };

    public static SlaStatus GetStatus(TaskItem task, DateTime nowUtc)
    {
        if (task.ParentTaskId.HasValue || task.DeletedAt.HasValue || !task.DueDate.HasValue)
        {
            return SlaStatus.NotTracked;
        }

        var due = task.DueDate.Value;
        if (task.IsCompleted)
        {
            if (!task.CompletedAt.HasValue)
            {
                return SlaStatus.NotTracked;
            }

            return task.CompletedAt.Value <= due ? SlaStatus.Met : SlaStatus.Breached;
        }

        return nowUtc > due ? SlaStatus.Breached : SlaStatus.OnTrack;
    }

    public static (int MetWeight, int TotalResolvedWeight, int MetCount, int BreachedCount, int OnTrackCount) Summarize(
        IEnumerable<TaskItem> tasks,
        DateTime nowUtc)
    {
        var metWeight = 0;
        var totalResolvedWeight = 0;
        var metCount = 0;
        var breachedCount = 0;
        var onTrackCount = 0;

        foreach (var task in tasks)
        {
            var status = GetStatus(task, nowUtc);
            var weight = GetPriorityWeight(task.Priority);
            switch (status)
            {
                case SlaStatus.Met:
                    metWeight += weight;
                    totalResolvedWeight += weight;
                    metCount++;
                    break;
                case SlaStatus.Breached:
                    totalResolvedWeight += weight;
                    breachedCount++;
                    break;
                case SlaStatus.OnTrack:
                    onTrackCount++;
                    break;
            }
        }

        return (metWeight, totalResolvedWeight, metCount, breachedCount, onTrackCount);
    }

    public static int? ComputeCompliancePercent(int metWeight, int totalResolvedWeight)
    {
        if (totalResolvedWeight <= 0)
        {
            return null;
        }

        return (int)Math.Round(100.0 * metWeight / totalResolvedWeight, MidpointRounding.AwayFromZero);
    }
}

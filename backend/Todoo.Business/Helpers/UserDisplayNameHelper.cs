using Todoo.Business.Models.Teams;
using Todoo.Entities.Entities;
using Todoo.Entities.Enums;

namespace Todoo.Business.Helpers;

public static class UserDisplayNameHelper
{
    public static string Format(User user)
    {
        var fullName = string.Join(
            ' ',
            new[] { user.FirstName, user.LastName }.Where(part => !string.IsNullOrWhiteSpace(part)));

        return string.IsNullOrWhiteSpace(fullName) ? user.Email : fullName.Trim();
    }

    public static Dictionary<string, string> BuildDisplayNameByEmail(IEnumerable<User> users)
    {
        return users.ToDictionary(user => user.Email, Format);
    }

    public static string? ResolveAssigneeLogValue(
        string? value,
        IReadOnlyDictionary<string, string> displayNameByEmail)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (value == "Kendine atandi")
        {
            return value;
        }

        return displayNameByEmail.GetValueOrDefault(value, value);
    }

    public static void ApplyAssigneeDisplayNames(
        TaskActivityLogDto log,
        IReadOnlyDictionary<string, string> displayNameByEmail)
    {
        if (log.ActionType != TaskActivityAction.Assigned)
        {
            return;
        }

        log.OldValue = ResolveAssigneeLogValue(log.OldValue, displayNameByEmail);
        log.NewValue = ResolveAssigneeLogValue(log.NewValue, displayNameByEmail);
    }
}

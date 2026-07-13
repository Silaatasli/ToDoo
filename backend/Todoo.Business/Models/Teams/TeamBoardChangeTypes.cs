namespace Todoo.Business.Models.Teams;

public static class TeamBoardChangeTypes
{
    public const string TaskCreated = "task-created";
    public const string TaskUpdated = "task-updated";
    public const string TaskMoved = "task-moved";
    public const string TaskDeleted = "task-deleted";
    public const string TaskAssigned = "task-assigned";
    public const string TaskAssignmentAccepted = "task-assignment-accepted";
    public const string TaskAssignmentDeclined = "task-assignment-declined";
    public const string ColumnAdded = "column-added";
    public const string ColumnUpdated = "column-updated";
    public const string ColumnsReordered = "columns-reordered";
    public const string BoardCreated = "board-created";
    public const string BoardDeleted = "board-deleted";
}

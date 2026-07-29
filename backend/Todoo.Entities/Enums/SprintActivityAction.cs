namespace Todoo.Entities.Enums;

public enum SprintActivityAction
{
    SprintCreated = 1,
    SprintUpdated = 2,
    SprintDeleted = 3,
    SprintStarted = 4,
    SprintCompleted = 5,
    SprintCancelled = 6,
    TaskAddedAfterSprintStart = 7,
    TaskRemovedAfterSprintStart = 8,
    SprintScopeChanged = 9,
    TaskAddedToSprint = 10,
    TaskRemovedFromSprint = 11
}

# ToDoo

Real-time team task manager with boards, sprints, backlog, SLA reports, and notifications.

Angular frontend + ASP.NET Core API. Teams work on Kanban boards, plan sprints, and get live updates.

## Features

- Register / login, JWT access + refresh tokens, logout (this device or all devices)
- Forgot / reset password (email)
- Personal workspace created automatically on register (`Kisisel Gorevlerim`)
- Teams: create, invite members, remove members
- Multiple boards per team, custom columns, drag-and-drop column order
- Kanban tasks: priority, category, dates, assignee
- Task assignment with accept / decline
- Subtasks
- Comments with replies and `@mentions`
- File attachments on tasks and comments
- Soft delete + restore for tasks
- Sprint / backlog (Kapsam): move tasks between backlog and sprint, start / complete / cancel
- One active sprint per board
- SLA tracking for active-sprint tasks with due dates (On track / Met / Breached, priority-weighted)
- Reports: task summary + SLA
- Team announcements (draft, scheduled, published)
- In-app notifications (assignment, mention, reply, new member, announcement, sprint started)
- Live board updates and live drag preview (SignalR)
- Global search (teams, boards, tasks, people)
- Profile (name, title, phone, photo)
- Dark mode
- Recent items

## Roles and permissions

There is no separate admin panel. Access is based on **team leader** vs **team member**.

### Any signed-in user
- Create a team (becomes the leader)
- Join only teams they belong to
- Edit their own profile
- Use personal board (not listed with other teams)

### Team member
- Open team boards, Kapsam, announcements, reports
- Create / edit / move tasks and subtasks
- Assign tasks; only the assignee can accept or decline
- Comment, mention teammates, attach files
- Restore a soft-deleted task (if still a member)
- Use sprint/backlog: add/remove tasks, start / complete / cancel sprint
- See own SLA and the team task summary
- See published announcements
- Receive notifications for that team

### Team leader (extra)
- Add / remove members (leader cannot be removed)
- Create / delete boards (last board cannot be deleted)
- Reorder columns
- Delete the team
- Grant or revoke **announcement publish** permission for members
- Publish announcements (always allowed for the leader)
- Delete any announcement
- See **all members’ SLA** on the reports page
- Delete any comment (members can delete their own comments, or comments on a task they created)

### Announcement publish permission
- Leader always has it
- Leader can turn it on/off per member
- Members with this permission can create / schedule / publish announcements
- Announcement author or leader can delete it

### Comments and files
- Comment author, task creator, or team leader can delete a comment
- Same idea for comment attachments
- Task attachments follow task access (team members)

## Tech stack

| Layer | Stack |
| --- | --- |
| Frontend | Angular 21 |
| Backend | ASP.NET Core (.NET 10), Entity Framework Core |
| Database | SQL Server |
| Realtime | SignalR |
| Sessions / notifications | Redis |
| Files | MinIO |
| Notification queue | RabbitMQ |
| Sprint audit search | OpenSearch |
| Global search | Lucene.NET |
| Email | SendGrid |

## Project structure

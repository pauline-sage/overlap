# Overlap

**Shared experiences, even when you are apart.**

Overlap is a personal full-stack project for two people in different locations. It brings shared activities, local outings and plans into a phone-oriented web application, with private invitations and data scoped to each two-person connection.

## What it does

- **Watch and read:** discover films, series and books; share picks, keep a shared bookshelf and record titles watched together.
- **Step out:** search for local venues using your own location, radius and time, then optionally share a place and respond to a partner's proposal. Search locations remain private.
- **Make and play:** cooking activities, drawing sessions, photo albums and quizzes with shared results.
- **Plan together:** shared appointments, birthdays and optional phone reminders, with timezone-aware display.
- **Manage your account:** verified-email registration, password recovery, profile photos, sharing preferences and account deletion.

Implemented features in this repository may be ahead of the hosted version. This is an evolving personal project; native mobile packaging and app-store releases remain future work.

## Engineering

The application uses a modular monolith: one Angular client and one ASP.NET Core API, backed by MySQL.

| Area | Technology |
| --- | --- |
| Client | Angular 22, TypeScript, HTML and SCSS |
| API | ASP.NET Core 9, C#, nullable reference types |
| Persistence | Entity Framework Core 9, Pomelo, MySQL 8.4 |
| Authentication | ASP.NET Core Identity, HttpOnly session cookies and antiforgery protection |
| Email | MailKit / local Mailpit; hosted Mailjet transport |
| Catalogue and places | TMDB, Open Library and Geoapify; optional YouTube search |
| Tests | Vitest; xUnit integration tests with isolated SQLite |
| Deployment | Docker, Render and Aiven MySQL |

Important design choices include connection-scoped authorization, server-side provider credentials, explicit sharing actions, expiring protected location tokens and timezone/DST validation. SQLite is used only for isolated tests; the application database is MySQL.

```text
apps/mobile/          Angular application and frontend tests
services/api/         API, feature modules, account handling and EF migrations
services/api.tests/   API integration and focused domain tests
scripts/              Local setup, administration and verification tools
deployment/           Hosted migration configuration example
```

## Run locally on Windows

Prerequisites: .NET 9 SDK, Node.js compatible with Angular 22, npm, Git and PowerShell. Commands below run from the repository root in Command Prompt unless otherwise stated.

For a **fresh checkout**, install the local MySQL and email-capture tools:

```cmd
powershell -NoProfile -File scripts\Install-LocalMySQL.ps1
powershell -NoProfile -File scripts\Install-Mailpit.ps1
dotnet tool restore
```

The MySQL script generates private local credentials and `services/api/appsettings.Local.json`. It refuses to overwrite an existing configuration or data directory. `.local/mysql/data` contains persistent database data.

Apply the migrations with MySQL running:

```cmd
scripts\start-mysql.cmd
cd services\api
dotnet ef database update
cd ..\..
```

Start the following in separate terminals:

```cmd
.local\mailpit\mailpit.exe --smtp 127.0.0.1:1025 --listen 127.0.0.1:8025 --disable-version-check
```

```cmd
dotnet run --project services\api
```

```cmd
cd apps\mobile
npm ci
npm start -- --host localhost --port 1201
```

| Service | Local address |
| --- | --- |
| App | http://localhost:1201 |
| API health | http://localhost:5217/api/health |
| Captured email | http://127.0.0.1:8025 |
| MySQL | `127.0.0.1:3307` |

Register your own account and use Mailpit for local confirmation emails. No application accounts or shared passwords are seeded. Mailpit captures test email; it does not deliver to real inboxes.

Catalogue and venue features require their respective configuration. See [the local configuration example](services/api/appsettings.Local.example.json) for `Streaming:TmdbReadAccessToken`, optional `Streaming:YouTubeApiKey` and `Outings:GeoapifyApiKey`. Keep real values in the ignored local configuration or server environment variables. Optional phone reminders have a separate [configuration example](services/api/appsettings.Push.example.json).

## Build and test

```cmd
dotnet build services\api
dotnet test services\api.tests
cd apps\mobile
npm run build
npm test -- --watch=false
```

The test commands above run the suites; during development, select the tests relevant to the changed feature. API tests use an isolated database and do not replace the application's MySQL database.

## Configuration and deployment

[Dockerfile](Dockerfile) and [render.yaml](render.yaml) define the hosting foundation. Production needs private database, email and provider configuration; database migrations are an explicit deployment step. [deployment/migration.example.json](deployment/migration.example.json) contains placeholders only.

Local configuration, credentials, database files and developer notes are intentionally excluded from Git. Do not commit filled configuration examples or private keys.

No license has been selected for this project's source. Third-party components and catalogue data retain their respective licenses and attribution requirements.

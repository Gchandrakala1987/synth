# API reference

Base URL: `http://localhost:5080` (dev) or `https://<app>.azurewebsites.net` (prod).

Swagger UI is available at `/swagger` in `Development`. The schemas below mirror the C# DTOs
in [`src/Synth.Api/Models/Models.cs`](../src/Synth.Api/Models/Models.cs).

## REST

### `POST /api/test-runs` — queue a run

```json
// Request
{
  "url": "https://demo.playwright.dev/todomvc",
  "instruction": "Add three todos and mark the second complete; verify the active counter shows 2.",
  "viewport": { "width": 1280, "height": 800 }
}
```

```json
// 202 Accepted
{
  "id": "f6c3a2c8-9f1e-4cf3-9a8d-3f4f2e0b3a11",
  "url": "https://demo.playwright.dev/todomvc",
  "instruction": "...",
  "status": "Queued",
  "createdAt": "2026-05-25T18:42:11.231Z",
  "startedAt": null,
  "finishedAt": null,
  "steps": [],
  "report": null
}
```

Validation:

- `url` must be a fully-qualified `http(s)` URL.
- `instruction` is required.
- `viewport` is optional; defaults to `1280x800`.

### `GET /api/test-runs/{id}` — current snapshot of a run

`200 OK` returns the same DTO as above with the latest `status`, `steps`, and (if finished) `report`.
`404 Not Found` if the id is unknown.

### `GET /api/test-runs` — list the 50 most recent runs

`200 OK` with `TestRunDto[]`, newest first.

### `GET /healthz` — liveness

```json
{ "status": "ok", "time": "2026-05-25T18:42:11.231Z" }
```

## SignalR

Hub URL: `/hubs/test-runs`.

### Server → Client

Method: `progress`, payload: `ProgressEvent`.

```ts
type ProgressEventKind =
  | "RunStarted"
  | "StepStarted"
  | "StepCompleted"
  | "StepFailed"
  | "RunFinished"
  | "LogMessage";

interface ProgressEvent {
  runId: string;
  kind: ProgressEventKind;
  at: string;                 // ISO timestamp
  step?: TestStep | null;
  status?: TestRunStatus | null;
  report?: BugReport | null;
  message?: string | null;
}
```

### Client → Server

| Method                    | Args      | Notes                                                                 |
|---------------------------|-----------|-----------------------------------------------------------------------|
| `SubscribeAsync(runId)`   | `Guid`    | Joins the per-run group. Re-invoke after a reconnect.                 |
| `UnsubscribeAsync(runId)` | `Guid`    | Leaves the group. Connections also drop their groups on disconnect.   |

### TypeScript client (excerpt from `web/src/lib/signalr.ts`)

```ts
const conn = new signalR.HubConnectionBuilder()
  .withUrl(`${apiBaseUrl}/hubs/test-runs`)
  .withAutomaticReconnect([0, 1_000, 5_000, 15_000])
  .build();

conn.on("progress", (evt: ProgressEvent) => onEvent(evt));

await conn.start();
await conn.invoke("SubscribeAsync", runId);
```

## Enums

```ts
type TestRunStatus = "Queued" | "Running" | "Passed" | "Failed" | "Errored" | "Cancelled";
type StepStatus    = "Started" | "Succeeded" | "Failed";
type BugSeverity   = "Info" | "Low" | "Medium" | "High" | "Critical";
```

## Error responses

| Status | Body                                | When                                          |
|--------|-------------------------------------|-----------------------------------------------|
| 400    | `{ "error": "<reason>" }`           | Validation failure on `POST /api/test-runs`.  |
| 404    | (empty)                             | Unknown run id on `GET /api/test-runs/{id}`.  |
| 500    | ASP.NET problem-details payload     | Unexpected — file an issue with the trace id. |

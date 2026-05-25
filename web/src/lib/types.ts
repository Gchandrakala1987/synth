// Mirror of the backend DTOs in src/Synth.Api/Models/Models.cs.
// Kept hand-written rather than codegen'd to keep the MVP simple — swap for
// NSwag/openapi-typescript when the surface grows.

export type TestRunStatus =
  | "Queued"
  | "Running"
  | "Passed"
  | "Failed"
  | "Errored"
  | "Cancelled";

export type StepStatus = "Started" | "Succeeded" | "Failed";

export type BugSeverity = "Info" | "Low" | "Medium" | "High" | "Critical";

export type ProgressEventKind =
  | "RunStarted"
  | "StepStarted"
  | "StepCompleted"
  | "StepFailed"
  | "RunFinished"
  | "LogMessage";

export interface TestStep {
  index: number;
  tool: string;
  description: string;
  status: StepStatus;
  at: string;
  detail?: string | null;
  screenshotBase64?: string | null;
}

export interface BugFinding {
  title: string;
  description: string;
  severity: BugSeverity;
  reproduction?: string | null;
  relatedStepIndex?: number | null;
}

export interface BugReport {
  summary: string;
  overallSeverity: BugSeverity;
  findings: BugFinding[];
  stepsExecuted: number;
  duration: string;
}

export interface TestRunDto {
  id: string;
  url: string;
  instruction: string;
  status: TestRunStatus;
  createdAt: string;
  startedAt?: string | null;
  finishedAt?: string | null;
  steps: TestStep[];
  report?: BugReport | null;
}

export interface ProgressEvent {
  runId: string;
  kind: ProgressEventKind;
  at: string;
  step?: TestStep | null;
  status?: TestRunStatus | null;
  report?: BugReport | null;
  message?: string | null;
}

export interface CreateTestRunRequest {
  url: string;
  instruction: string;
}

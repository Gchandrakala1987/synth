import { useEffect, useState } from "react";
import { TestForm, type TestFormValues } from "./components/TestForm";
import { ProgressFeed } from "./components/ProgressFeed";
import { BugReportView } from "./components/BugReportView";
import { createTestRun, getTestRun } from "./lib/api";
import { subscribeToRun } from "./lib/signalr";
import type { TestRunDto, ProgressEvent, TestStep } from "./lib/types";

export default function App() {
  const [run, setRun] = useState<TestRunDto | null>(null);
  const [steps, setSteps] = useState<TestStep[]>([]);
  const [connectionState, setConnectionState] = useState("Disconnected");
  const [error, setError] = useState<string | null>(null);

  // Subscribe to live progress whenever we have an active run.
  const runId = run?.id;
  // Subscribe to live progress whenever we have an active run.
  useEffect(() => {
    if (!runId) return;

    const stop = subscribeToRun(
      runId,
      (evt: ProgressEvent) => {
        // Step lifecycle events — merge into local state by step index.
        if (evt.step) {
          setSteps((prev) => {
            const next = [...prev];
            const i = next.findIndex((s) => s.index === evt.step!.index);
            if (i === -1) next.push(evt.step!);
            else next[i] = evt.step!;
            return next.sort((a, b) => a.index - b.index);
          });
        }
        // Terminal events update the run snapshot so the report renders.
        if (evt.kind === "RunFinished") {
          getTestRun(runId).then(setRun).catch(console.error);
        }
      },
      (state) => setConnectionState(state)
    );

    return () => stop();
  }, [runId]);

  const busy =
    run !== null && (run.status === "Queued" || run.status === "Running");

  async function handleSubmit(values: TestFormValues) {
    setError(null);
    setSteps([]);
    try {
      const created = await createTestRun(values);
      setRun(created);
    } catch (e) {
      setError((e as Error).message);
    }
  }

  return (
    <div className="app">
      <header className="hero">
        <h1>Synth</h1>
        <p className="tagline">
          AI-generated synthetic browser tests, in plain English.
        </p>
        <p>
          Describe what you want tested. The agent opens a real browser, clicks
          through the flow, and ships a structured bug report.
        </p>
      </header>

      <main className="grid">
        <TestForm busy={busy} onSubmit={handleSubmit} />

        {error && <div className="card error">⚠ {error}</div>}

        {run && (
          <>
            <ProgressFeed
              steps={steps.length ? steps : run.steps}
              connectionState={connectionState}
            />
            <BugReportView status={run.status} report={run.report ?? null} />
          </>
        )}
      </main>

      <footer>
        <small>
          Open source ·{" "}
          <a href="https://github.com/Gchandrakala1987/synth">GitHub</a>
        </small>
      </footer>
    </div>
  );
}

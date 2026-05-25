import type { TestStep } from "../lib/types";

interface Props {
  steps: TestStep[];
  connectionState: string;
}

export function ProgressFeed({ steps, connectionState }: Props) {
  return (
    <div className="card">
      <header className="card-head">
        <h2>Live progress</h2>
        <span className={`pill state-${connectionState.toLowerCase()}`}>{connectionState}</span>
      </header>

      {steps.length === 0 ? (
        <p className="muted">Waiting for the agent to take its first action…</p>
      ) : (
        <ol className="step-list">
          {steps.map((step) => (
            <li key={step.index} className={`step status-${step.status.toLowerCase()}`}>
              <div className="step-row">
                <span className="step-index">#{step.index + 1}</span>
                <code className="step-tool">{step.tool}</code>
                <span className="step-desc">{step.description}</span>
                <span className="step-status">{step.status}</span>
              </div>
              {step.detail && <pre className="step-detail">{step.detail}</pre>}
            </li>
          ))}
        </ol>
      )}
    </div>
  );
}

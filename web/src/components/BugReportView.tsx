import type { BugReport, TestRunStatus } from "../lib/types";

interface Props {
  status: TestRunStatus;
  report: BugReport | null | undefined;
}

const SEVERITY_RANK = ["Info", "Low", "Medium", "High", "Critical"] as const;

export function BugReportView({ status, report }: Props) {
  if (!report) {
    return (
      <div className="card">
        <h2>Report</h2>
        <p className="muted">The report will appear here when the run finishes.</p>
      </div>
    );
  }

  const sorted = [...report.findings].sort(
    (a, b) => SEVERITY_RANK.indexOf(b.severity) - SEVERITY_RANK.indexOf(a.severity)
  );

  return (
    <div className="card">
      <header className="card-head">
        <h2>Report</h2>
        <span className={`pill status-${status.toLowerCase()}`}>{status}</span>
      </header>

      <p className="summary">{report.summary}</p>

      <dl className="meta">
        <div><dt>Overall severity</dt><dd>{report.overallSeverity}</dd></div>
        <div><dt>Steps executed</dt><dd>{report.stepsExecuted}</dd></div>
        <div><dt>Findings</dt><dd>{report.findings.length}</dd></div>
      </dl>

      {sorted.length === 0 ? (
        <p className="muted">No findings — looks good.</p>
      ) : (
        <ul className="finding-list">
          {sorted.map((f, i) => (
            <li key={i} className={`finding sev-${f.severity.toLowerCase()}`}>
              <header>
                <span className={`pill sev-${f.severity.toLowerCase()}`}>{f.severity}</span>
                <h3>{f.title}</h3>
              </header>
              <p>{f.description}</p>
              {f.reproduction && (
                <details>
                  <summary>Reproduction</summary>
                  <pre>{f.reproduction}</pre>
                </details>
              )}
              {typeof f.relatedStepIndex === "number" && (
                <small className="muted">Related step: #{f.relatedStepIndex + 1}</small>
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

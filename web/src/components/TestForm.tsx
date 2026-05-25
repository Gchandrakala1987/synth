import { useState, type FormEvent } from "react";

export interface TestFormValues {
  url: string;
  instruction: string;
}

const EXAMPLES: TestFormValues[] = [
  { url: "https://example.com", instruction: "Verify the page loads and contains the heading 'Example Domain'." },
  { url: "https://www.wikipedia.org", instruction: "Search for 'Playwright (software)' and confirm the article opens." },
  { url: "https://demo.playwright.dev/todomvc", instruction: "Add three todos, mark the second one complete, and verify the active counter shows 2." }
];

interface Props {
  busy: boolean;
  onSubmit(values: TestFormValues): void;
}

export function TestForm({ busy, onSubmit }: Props) {
  const [url, setUrl] = useState("https://demo.playwright.dev/todomvc");
  const [instruction, setInstruction] = useState(
    "Add three todos, mark the second one complete, and verify the active counter shows 2."
  );

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!busy) onSubmit({ url: url.trim(), instruction: instruction.trim() });
  }

  return (
    <form className="card" onSubmit={handleSubmit}>
      <h2>New test run</h2>

      <label htmlFor="url">Website URL</label>
      <input
        id="url"
        type="url"
        required
        value={url}
        onChange={(e) => setUrl(e.target.value)}
        placeholder="https://example.com"
        disabled={busy}
      />

      <label htmlFor="instruction">What should the agent test?</label>
      <textarea
        id="instruction"
        required
        rows={4}
        value={instruction}
        onChange={(e) => setInstruction(e.target.value)}
        placeholder="e.g. Test the checkout flow with a guest user."
        disabled={busy}
      />

      <div className="form-row">
        <button type="submit" disabled={busy || !url || !instruction}>
          {busy ? "Running…" : "Run agent"}
        </button>

        <div className="examples">
          <span>Try:</span>
          {EXAMPLES.map((ex, i) => (
            <button
              key={i}
              type="button"
              className="link"
              disabled={busy}
              onClick={() => {
                setUrl(ex.url);
                setInstruction(ex.instruction);
              }}
            >
              Example {i + 1}
            </button>
          ))}
        </div>
      </div>
    </form>
  );
}

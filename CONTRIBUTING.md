# Contributing

Thanks for your interest in improving Synth.

## Ground rules

- Open an issue before you start work on anything non-trivial — it saves rework if the design needs to change.
- Keep PRs small and focused. A 200-line PR gets merged; a 2,000-line PR sits.
- All new behavior needs a test. Unit tests for orchestrator/agent logic, integration tests for Playwright tool calls.
- Follow the style conventions in `.editorconfig`. Run `dotnet format` and `npm run lint` before pushing.

## Local development

See [README.md](./README.md#quickstart) for the full quickstart. The short version:

```bash
# Backend
cd src/Synth.Api
dotnet restore
dotnet run

# Frontend (separate terminal)
cd web
npm install
npm run dev
```

Set `OPENAI__APIKEY` (or the Azure equivalents) in `.env` or your shell.

## Pull request checklist

- [ ] `dotnet build` succeeds with no new warnings
- [ ] `dotnet test` passes
- [ ] `npm run build` succeeds in `web/`
- [ ] New public APIs have XML doc comments
- [ ] README / docs updated if behavior changed
- [ ] No secrets in code or commit history

## Reporting bugs

Open an issue with:

1. What you expected to happen
2. What actually happened
3. Minimal repro (URL + instruction)
4. Logs (redact secrets)

## Areas that need help

See the roadmap in the README. Good first issues are tagged `good-first-issue`.

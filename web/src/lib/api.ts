import type { CreateTestRunRequest, TestRunDto } from "./types";

const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/$/, "") ?? "";

export async function createTestRun(req: CreateTestRunRequest): Promise<TestRunDto> {
  const res = await fetch(`${API_BASE}/api/test-runs`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(req)
  });
  if (!res.ok) {
    const body = await res.text();
    throw new Error(`createTestRun failed (${res.status}): ${body}`);
  }
  return (await res.json()) as TestRunDto;
}

export async function getTestRun(id: string): Promise<TestRunDto> {
  const res = await fetch(`${API_BASE}/api/test-runs/${id}`);
  if (!res.ok) throw new Error(`getTestRun failed (${res.status})`);
  return (await res.json()) as TestRunDto;
}

export const apiBaseUrl = API_BASE;

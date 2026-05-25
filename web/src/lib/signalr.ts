import * as signalR from "@microsoft/signalr";
import type { ProgressEvent } from "./types";
import { apiBaseUrl } from "./api";

/**
 * Open a SignalR connection scoped to one test run. The hook returns an
 * unsubscribe function that tears down the connection — call it on unmount.
 */
export function subscribeToRun(
  runId: string,
  onEvent: (evt: ProgressEvent) => void,
  onStateChange?: (state: signalR.HubConnectionState) => void
): () => void {
  const conn = new signalR.HubConnectionBuilder()
    .withUrl(`${apiBaseUrl}/hubs/test-runs`)
    .withAutomaticReconnect([0, 1_000, 5_000, 15_000])
    .configureLogging(signalR.LogLevel.Warning)
    .build();

  conn.on("progress", (evt: ProgressEvent) => onEvent(evt));

  conn.onreconnecting(() => onStateChange?.(conn.state));
  conn.onreconnected(async () => {
    onStateChange?.(conn.state);
    // Re-join the group after a reconnect — SignalR does NOT preserve group membership.
    await conn.invoke("SubscribeAsync", runId);
  });
  conn.onclose(() => onStateChange?.(conn.state));

  let stopped = false;
  (async () => {
    try {
      await conn.start();
      if (stopped) {
        await conn.stop();
        return;
      }
      onStateChange?.(conn.state);
      await conn.invoke("SubscribeAsync", runId);
    } catch (err) {
      console.error("SignalR connect failed", err);
    }
  })();

  return () => {
    stopped = true;
    conn.stop().catch(() => { /* noop */ });
  };
}

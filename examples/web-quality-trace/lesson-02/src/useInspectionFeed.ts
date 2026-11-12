import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { useEffect, useState } from "react";

export function useInspectionFeed(enabled: boolean) {
  const [status, setStatus] = useState<"connecting" | "connected" | "reconnecting">("connecting");
  const [revision, setRevision] = useState(0);

  useEffect(() => {
    if (!enabled) return;
    let disposed = false;
    let retry: number | undefined;
    const connection = new HubConnectionBuilder()
      .withUrl("/api/live")
      .withAutomaticReconnect({ nextRetryDelayInMilliseconds: () => 2000 })
      .configureLogging(LogLevel.Warning)
      .build();

    function refresh() {
      if (!disposed) setRevision(value => value + 1);
    }

    function connected() {
      if (disposed) return;
      setStatus("connected");
      refresh();
    }

    async function start() {
      try {
        await connection.start();
        connected();
      } catch {
        if (disposed) return;
        setStatus("reconnecting");
        retry = window.setTimeout(() => void start(), 2000);
      }
    }

    connection.on("InspectionsChanged", refresh);
    connection.onreconnecting(() => { if (!disposed) setStatus("reconnecting"); });
    connection.onreconnected(connected);
    const safetyRefresh = window.setInterval(refresh, 15000);
    void start();

    return () => {
      disposed = true;
      window.clearTimeout(retry);
      window.clearInterval(safetyRefresh);
      void connection.stop().catch(() => {});
    };
  }, [enabled]);

  return { status, revision };
}

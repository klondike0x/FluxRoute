import { useEffect, useState } from "react";

import { BackendStatus, fetchBackendStatus } from "./api/status";

function App() {
  const [backendStatus, setBackendStatus] = useState<BackendStatus | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    fetchBackendStatus()
      .then((result) => {
        if (!cancelled) {
          setBackendStatus(result);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setError("ошибка подключения к бэкенду");
        }
      });

    return () => {
      cancelled = true;
    };
  }, []);

  const statusText = error ?? backendStatus?.status ?? "загрузка...";
  const engineText =
    backendStatus?.engine === "not_initialized"
      ? "не инициализирован"
      : backendStatus?.engine ?? "загрузка...";

  return (
    <main className="app-shell">
      <h1>FluxRoute 2.0</h1>
      <p>
        Статус бэкенда: <strong>{statusText}</strong>
      </p>
      <p>
        Версия API: <strong>{backendStatus?.version ?? "—"}</strong>
      </p>
      <p>
        Engine: <strong>{engineText}</strong>
      </p>
    </main>
  );
}

export default App;

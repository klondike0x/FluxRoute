import { useEffect, useState } from "react";
import axios from "axios";

function App() {
  const [status, setStatus] = useState<string>("загрузка...");

  useEffect(() => {
    axios
      .get("http://localhost:8000/api/status")
      .then((response) => setStatus(response.data.status))
      .catch(() => setStatus("ошибка подключения к бэкенду"));
  }, []);

  return (
    <main className="app-shell">
      <h1>FluxRoute 2.0</h1>
      <p>
        Статус бэкенда: <strong>{status}</strong>
      </p>
    </main>
  );
}

export default App;

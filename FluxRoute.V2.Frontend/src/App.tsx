import { useEffect, useState } from "react";

import { fetchProfiles, type ProfileItem } from "./api/profiles";
import { fetchBackendStatus } from "./api/status";
import type { BackendStatus } from "./api/status";

function App() {
  const [backendStatus, setBackendStatus] = useState<BackendStatus | null>(null);
  const [profiles, setProfiles] = useState<ProfileItem[]>([]);
  const [statusError, setStatusError] = useState<string | null>(null);
  const [profilesError, setProfilesError] = useState<string | null>(null);

  useEffect(() => {
    fetchBackendStatus()
      .then(setBackendStatus)
      .catch(() => setStatusError("ошибка подключения к бэкенду"));

    fetchProfiles()
      .then((result) => setProfiles(result.items))
      .catch(() => setProfilesError("не удалось загрузить профили"));
  }, []);

  const statusText = statusError ?? backendStatus?.status ?? "загрузка...";
  const engineText =
    backendStatus?.engine === "not_initialized"
      ? "не инициализирован"
      : backendStatus?.engine ?? "загрузка...";

  return (
    <main className="app-shell">
      <section className="status-card">
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
      </section>

      <section className="profiles-card">
        <div className="section-heading">
          <h2>Профили</h2>
          <span>{profiles.length}</span>
        </div>

        {profilesError ? (
          <p className="error-text">{profilesError}</p>
        ) : profiles.length === 0 ? (
          <p className="muted-text">
            В папке engine пока нет .bat-профилей.
          </p>
        ) : (
          <ul className="profile-list">
            {profiles.map((profile) => (
              <li key={profile.file_name}>
                <span>{profile.display_name}</span>
                <small>
                  {profile.source}
                  {profile.is_user_copy ? " · пользовательская копия" : ""}
                </small>
              </li>
            ))}
          </ul>
        )}
      </section>
    </main>
  );
}

export default App;

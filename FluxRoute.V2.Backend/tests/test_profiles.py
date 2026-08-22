from pathlib import Path

from fastapi.testclient import TestClient

from app.main import app
from app.routes.profiles import get_profile_service
from app.services.profile_service import ProfileService


def test_profiles_use_ai_evolved_precedence_and_skip_service(tmp_path: Path) -> None:
    engine_dir = tmp_path / "engine"
    evolved_dir = engine_dir / "ai-evolved"
    evolved_dir.mkdir(parents=True)

    (engine_dir / "general.bat").write_text("echo general", encoding="utf-8")
    (engine_dir / "duplicate.bat").write_text("echo old", encoding="utf-8")
    (engine_dir / "service.bat").write_text("echo service", encoding="utf-8")
    (evolved_dir / "duplicate.bat").write_text("echo evolved", encoding="utf-8")
    (evolved_dir / "custom_copy2.bat").write_text("echo copy", encoding="utf-8")

    profiles = ProfileService(engine_dir).list_profiles()

    assert [profile.file_name for profile in profiles] == [
        "custom_copy2.bat",
        "duplicate.bat",
        "general.bat",
    ]
    assert profiles[1].source == "ai-evolved"
    assert profiles[0].is_user_copy is True


def test_profiles_endpoint_returns_engine_profiles(tmp_path: Path) -> None:
    engine_dir = tmp_path / "engine"
    engine_dir.mkdir()
    (engine_dir / "general.bat").write_text("echo general", encoding="utf-8")

    app.dependency_overrides[get_profile_service] = lambda: ProfileService(engine_dir)
    try:
        response = TestClient(app).get("/api/profiles")
    finally:
        app.dependency_overrides.clear()

    assert response.status_code == 200
    assert response.json() == {
        "items": [
            {
                "file_name": "general.bat",
                "display_name": "general",
                "source": "engine",
                "is_user_copy": False,
            }
        ],
        "count": 1,
    }

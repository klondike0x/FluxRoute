from fastapi.testclient import TestClient

from app.main import app


client = TestClient(app)


def test_status_endpoint() -> None:
    response = client.get("/api/status")

    assert response.status_code == 200
    assert response.json() == {
        "status": "ok",
        "version": "2.0.0-dev",
        "engine": "not_initialized",
    }

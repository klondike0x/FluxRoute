"""Start the FluxRoute 2.0 FastAPI backend from Visual Studio (F5)."""

import sys
from pathlib import Path

import uvicorn


BACKEND_DIR = Path(__file__).resolve().parent


def main() -> None:
    sys.path.insert(0, str(BACKEND_DIR))
    uvicorn.run(
        "app.main:app",
        host="127.0.0.1",
        port=8000,
        reload=False,
    )


if __name__ == "__main__":
    main()

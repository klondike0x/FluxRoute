"""Start the FluxRoute 2.0 development stack from one F5 launcher."""

import os
import subprocess
import sys
from pathlib import Path

import uvicorn


BACKEND_DIR = Path(__file__).resolve().parent
FRONTEND_DIR = BACKEND_DIR.parent / "FluxRoute.V2.Frontend"


def start_frontend() -> subprocess.Popen[bytes]:
    npm_command = "npm.cmd" if os.name == "nt" else "npm"
    return subprocess.Popen(
        [npm_command, "run", "dev", "--", "--host", "127.0.0.1"],
        cwd=FRONTEND_DIR,
    )


def stop_frontend(process: subprocess.Popen[bytes]) -> None:
    if process.poll() is not None:
        return

    if os.name == "nt":
        subprocess.run(
            ["taskkill", "/PID", str(process.pid), "/T", "/F"],
            check=False,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
    else:
        process.terminate()
        process.wait(timeout=5)


def main() -> None:
    sys.path.insert(0, str(BACKEND_DIR))

    if not FRONTEND_DIR.exists():
        raise FileNotFoundError(f"Frontend project not found: {FRONTEND_DIR}")

    frontend_process = start_frontend()
    print("FluxRoute frontend started on http://127.0.0.1:5173", flush=True)

    try:
        uvicorn.run(
            "app.main:app",
            host="127.0.0.1",
            port=8000,
            reload=False,
        )
    finally:
        stop_frontend(frontend_process)


if __name__ == "__main__":
    main()

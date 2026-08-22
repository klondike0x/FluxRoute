"""Start the FluxRoute 2.0 development stack from Visual Studio (F5)."""

import shutil
import subprocess
import sys
from pathlib import Path

import uvicorn


BACKEND_DIR = Path(__file__).resolve().parent
FRONTEND_DIR = BACKEND_DIR.parent / "frontend"


def stop_process(process: subprocess.Popen[bytes]) -> None:
    if process.poll() is not None:
        return

    process.terminate()
    try:
        process.wait(timeout=5)
    except subprocess.TimeoutExpired:
        process.kill()
        process.wait()


def main() -> None:
    npm = shutil.which("npm.cmd") or shutil.which("npm")
    if npm is None:
        raise RuntimeError("npm не найден в PATH. Установите Node.js перед запуском.")

    frontend_process = subprocess.Popen(
        [npm, "run", "dev", "--", "--host", "127.0.0.1"],
        cwd=FRONTEND_DIR,
    )

    try:
        sys.path.insert(0, str(BACKEND_DIR))
        uvicorn.run(
            "app.main:app",
            host="127.0.0.1",
            port=8000,
            reload=False,
        )
    finally:
        stop_process(frontend_process)


if __name__ == "__main__":
    main()

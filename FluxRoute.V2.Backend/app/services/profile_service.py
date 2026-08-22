from pathlib import Path

from app.models.profile import ProfileItem


class ProfileService:
    """Read strategy profiles from the engine directory.

    The precedence matches the old C# application: a file from
    ``engine/ai-evolved`` replaces an engine profile with the same name.
    """

    def __init__(self, engine_dir: Path | None = None) -> None:
        self.engine_dir = engine_dir or Path(__file__).resolve().parents[3] / "engine"

    def list_profiles(self) -> list[ProfileItem]:
        profiles: dict[str, ProfileItem] = {}

        self._collect(profiles, self.engine_dir, source="engine")
        self._collect(profiles, self.engine_dir / "ai-evolved", source="ai-evolved")

        return sorted(profiles.values(), key=lambda item: item.file_name.casefold())

    @staticmethod
    def _collect(
        profiles: dict[str, ProfileItem],
        directory: Path,
        *,
        source: str,
    ) -> None:
        if not directory.is_dir():
            return

        for path in directory.glob("*.bat"):
            if path.name.casefold() == "service.bat":
                continue

            stem = path.stem
            profiles[path.name.casefold()] = ProfileItem(
                file_name=path.name,
                display_name=stem,
                source=source,
                is_user_copy=ProfileService._is_user_copy(path.name),
            )

    @staticmethod
    def _is_user_copy(file_name: str) -> bool:
        stem = Path(file_name).stem
        marker = stem.lower().rfind("_copy")
        if marker < 0:
            return False

        suffix = stem[marker + len("_copy") :]
        return suffix == "" or suffix.isdigit()

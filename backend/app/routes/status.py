from fastapi import APIRouter


router = APIRouter()


@router.get("/status")
async def get_status() -> dict[str, str]:
    return {"status": "ok", "version": "2.0.0-dev"}

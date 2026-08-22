from fastapi import APIRouter, Depends

from app.models.profile import ProfileListResponse
from app.services.profile_service import ProfileService

router = APIRouter()


def get_profile_service() -> ProfileService:
    return ProfileService()


@router.get("/profiles", response_model=ProfileListResponse)
async def get_profiles(
    service: ProfileService = Depends(get_profile_service),
) -> ProfileListResponse:
    items = service.list_profiles()
    return ProfileListResponse(items=items, count=len(items))

from pydantic import BaseModel, Field


class ProfileItem(BaseModel):
    file_name: str
    display_name: str
    source: str = Field(description="engine or ai-evolved")
    is_user_copy: bool = False


class ProfileListResponse(BaseModel):
    items: list[ProfileItem]
    count: int

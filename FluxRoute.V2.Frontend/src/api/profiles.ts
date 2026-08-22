import { apiClient } from "./client";

export type ProfileItem = {
  file_name: string;
  display_name: string;
  source: "engine" | "ai-evolved";
  is_user_copy: boolean;
};

export type ProfileListResponse = {
  items: ProfileItem[];
  count: number;
};

export async function fetchProfiles(): Promise<ProfileListResponse> {
  const response = await apiClient.get<ProfileListResponse>("/profiles");
  return response.data;
}

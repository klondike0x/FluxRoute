import { apiClient } from "./client";

export type BackendStatus = {
  status: string;
  version: string;
  engine: string;
};

export async function fetchBackendStatus(): Promise<BackendStatus> {
  const response = await apiClient.get<BackendStatus>("/status");
  return response.data;
}

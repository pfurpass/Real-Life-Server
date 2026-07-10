import axios, { type AxiosInstance } from 'axios';
import type { AuthResult } from '../types';

const ACCESS_TOKEN_KEY = 'rls.accessToken';
const REFRESH_TOKEN_KEY = 'rls.refreshToken';

export function getAccessToken(): string | null {
  return localStorage.getItem(ACCESS_TOKEN_KEY);
}

export function storeAuthResult(result: AuthResult): void {
  localStorage.setItem(ACCESS_TOKEN_KEY, result.accessToken);
  localStorage.setItem(REFRESH_TOKEN_KEY, result.refreshToken);
}

export function clearAuth(): void {
  localStorage.removeItem(ACCESS_TOKEN_KEY);
  localStorage.removeItem(REFRESH_TOKEN_KEY);
}

export const apiClient: AxiosInstance = axios.create({ baseURL: '/api' });

apiClient.interceptors.request.use((config) => {
  const token = getAccessToken();
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

let refreshInFlight: Promise<string | null> | null = null;

async function refreshAccessToken(): Promise<string | null> {
  const refreshToken = localStorage.getItem(REFRESH_TOKEN_KEY);
  if (!refreshToken) {
    return null;
  }

  try {
    const response = await axios.post<AuthResult>('/api/auth/refresh', { refreshToken });
    storeAuthResult(response.data);
    return response.data.accessToken;
  } catch {
    clearAuth();
    return null;
  }
}

// Transparent refresh-on-401: a single in-flight refresh is shared across concurrent requests
// that all hit an expired access token at once.
apiClient.interceptors.response.use(
  (response) => response,
  async (error) => {
    const originalRequest = error.config;
    if (error.response?.status === 401 && !originalRequest._retried) {
      originalRequest._retried = true;
      refreshInFlight ??= refreshAccessToken().finally(() => {
        refreshInFlight = null;
      });

      const newToken = await refreshInFlight;
      if (newToken) {
        originalRequest.headers.Authorization = `Bearer ${newToken}`;
        return apiClient(originalRequest);
      }

      clearAuth();
      window.location.href = '/login';
    }
    return Promise.reject(error);
  }
);

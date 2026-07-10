import axios, { type AxiosInstance } from 'axios';
import type { AuthResult } from '../types';

/**
 * Extracts a readable message from an API error response. Two distinct shapes exist:
 *  - ExceptionHandlingMiddleware's own `{ status, title }` for exceptions thrown in a command
 *    handler (e.g. ValidationException) - `title` is already the specific message.
 *  - ASP.NET Core's automatic `ValidationProblemDetails` for model-binding failures (invalid
 *    JSON, a JSON value that doesn't convert to the target type/enum) - these never reach the
 *    controller or ExceptionHandlingMiddleware at all, so `title` alone is just the generic
 *    "One or more validation errors occurred." with the actual reason sitting in `errors`.
 * Only ever relays text the server itself produced - never echoes request field values, so this
 * cannot leak a submitted stream key even if a field named e.g. "streamKey" appears as a key.
 */
export function extractErrorMessage(err: unknown, fallback: string): string {
  if (!axios.isAxiosError(err)) {
    return fallback;
  }

  const data: unknown = err.response?.data;
  if (data && typeof data === 'object') {
    const errors = (data as { errors?: unknown }).errors;
    if (errors && typeof errors === 'object') {
      const messages = Object.entries(errors as Record<string, unknown>).flatMap(([field, value]) =>
        Array.isArray(value) ? value.filter((m): m is string => typeof m === 'string').map((m) => `${field}: ${m}`) : []
      );
      if (messages.length > 0) {
        return messages.join(' ');
      }
    }

    const title = (data as { title?: unknown }).title;
    if (typeof title === 'string' && title.length > 0) {
      return title;
    }
  }

  return fallback;
}

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

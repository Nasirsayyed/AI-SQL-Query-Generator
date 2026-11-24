import axios, { AxiosError, type AxiosRequestConfig, type InternalAxiosRequestConfig } from 'axios';
import { config } from '../config';
import { tokenStore } from '../auth/tokenStore';
import type { ApiEnvelope, ApiErrorItem, AuthResponse } from './types';

/** Normalised API error carrying the server's user-safe message and stable error code. */
export class ApiError extends Error {
  readonly status: number;
  readonly code: string;
  readonly errors: ApiErrorItem[];
  readonly data?: unknown;
  readonly traceId?: string | null;

  constructor(message: string, status: number, code: string, errors: ApiErrorItem[] = [], data?: unknown, traceId?: string | null) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
    this.errors = errors;
    this.data = data;
    this.traceId = traceId;
  }
}

export const http = axios.create({
  baseURL: config.apiBaseUrl,
  withCredentials: true,
  timeout: 180_000,
  headers: { 'X-Requested-With': 'XMLHttpRequest' },
});

http.interceptors.request.use((request: InternalAxiosRequestConfig) => {
  const token = tokenStore.get();
  if (token) request.headers.set('Authorization', `Bearer ${token}`);
  return request;
});

let refreshPromise: Promise<string | null> | null = null;
let onSessionExpired: (() => void) | null = null;

export function setSessionExpiredHandler(handler: () => void) {
  onSessionExpired = handler;
}

/** Exchanges the HttpOnly refresh cookie for a new access token. Concurrent callers share one request. */
export function refreshAccessToken(): Promise<string | null> {
  refreshPromise ??= axios
    .post<ApiEnvelope<AuthResponse>>(`${config.apiBaseUrl}/api/auth/refresh`, null, {
      withCredentials: true,
      headers: { 'X-Requested-With': 'XMLHttpRequest' },
    })
    .then((r) => {
      tokenStore.set(r.data.data.accessToken);
      return r.data.data.accessToken;
    })
    .catch(() => {
      tokenStore.set(null);
      return null;
    })
    .finally(() => {
      refreshPromise = null;
    });
  return refreshPromise;
}

http.interceptors.response.use(
  (response) => response,
  async (error: AxiosError<ApiEnvelope<unknown>>) => {
    const original = error.config as (AxiosRequestConfig & { _retried?: boolean }) | undefined;
    const isAuthCall = original?.url?.includes('/api/auth/');
    if (error.response?.status === 401 && original && !original._retried && !isAuthCall) {
      original._retried = true;
      const token = await refreshAccessToken();
      if (token) return http(original);
      onSessionExpired?.();
    }
    return Promise.reject(toApiError(error));
  },
);

export function toApiError(error: unknown): ApiError {
  if (error instanceof ApiError) return error;
  if (axios.isAxiosError(error)) {
    const body = error.response?.data as ApiEnvelope<unknown> | undefined;
    if (error.code === 'ECONNABORTED') return new ApiError('The request timed out. Please try again.', 0, 'TIMEOUT');
    if (axios.isCancel(error)) return new ApiError('The request was cancelled.', 0, 'CANCELLED');
    if (!error.response) return new ApiError('The server could not be reached. Check your connection.', 0, 'NETWORK_ERROR');
    return new ApiError(
      body?.message || `Request failed (${error.response.status}).`,
      error.response.status,
      body?.errorCode || 'UNKNOWN',
      body?.errors ?? [],
      body?.data,
      body?.traceId,
    );
  }
  return new ApiError(error instanceof Error ? error.message : 'Unexpected error.', 0, 'UNKNOWN');
}

/** Performs a request and unwraps the ApiResponse envelope. */
export async function request<T>(cfg: AxiosRequestConfig): Promise<T> {
  const response = await http.request<ApiEnvelope<T>>(cfg);
  return response.data.data;
}

export const api = {
  get: <T>(url: string, params?: object, signal?: AbortSignal) => request<T>({ method: 'GET', url, params, signal }),
  post: <T>(url: string, data?: unknown, signal?: AbortSignal) => request<T>({ method: 'POST', url, data, signal }),
  put: <T>(url: string, data?: unknown) => request<T>({ method: 'PUT', url, data }),
  delete: <T>(url: string) => request<T>({ method: 'DELETE', url }),
};

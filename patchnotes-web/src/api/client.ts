if (!import.meta.env.VITE_API_URL) {
  throw new Error("VITE_API_URL environment variable is required");
}

/** Base URL without /api — used by custom-fetch where Orval URLs already include /api */
export const API_ROOT = import.meta.env.VITE_API_URL as string;

const API_BASE_URL = `${API_ROOT}/api`;

export class ApiError extends Error {
  status: number;
  data?: unknown;
  isNetworkError: boolean;

  constructor(status: number, message: string, data?: unknown, isNetworkError = false) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.data = data;
    this.isNetworkError = isNetworkError;
  }
}

export function isApiError(error: unknown): error is ApiError {
  return error instanceof ApiError;
}

export function getErrorMessage(error: unknown): string {
  if (isApiError(error)) {
    if (error.isNetworkError) {
      return "Unable to connect to server. Please check your internet connection.";
    }
    if (error.status === 401) {
      return "Authentication required. Please sign in to continue.";
    }
    if (error.status === 403) {
      return "Access denied. You do not have permission for this action.";
    }
    if (error.status === 404) {
      return "The requested resource was not found.";
    }
    if (error.status >= 500) {
      return "Server error. Please try again later.";
    }
    return error.message || "An unexpected error occurred.";
  }
  if (error instanceof Error) {
    return error.message;
  }
  return "An unexpected error occurred.";
}

interface RequestOptions extends Omit<RequestInit, "body"> {
  body?: unknown;
}

async function request<T>(endpoint: string, options: RequestOptions = {}): Promise<T> {
  const { body, headers, ...rest } = options;

  // See the note in custom-fetch.ts: object-spreading a HeadersInit drops a
  // Headers instance entirely and turns an array of pairs into index keys.
  const mergedHeaders = new Headers({ "Content-Type": "application/json" });
  new Headers(headers).forEach((value, key) => mergedHeaders.set(key, value));

  const config: RequestInit = {
    ...rest,
    credentials: "include", // Include cookies for Stytch session auth
    headers: mergedHeaders,
  };

  if (body !== undefined) {
    config.body = JSON.stringify(body);
  }

  let response: Response;
  try {
    response = await fetch(`${API_BASE_URL}${endpoint}`, config);
  } catch (error) {
    // Network error (DNS failure, no internet, CORS, etc.)
    throw new ApiError(
      0,
      error instanceof Error ? error.message : "Network request failed",
      undefined,
      true,
    );
  }

  if (!response.ok) {
    const data = await response.json().catch(() => null);
    throw new ApiError(response.status, response.statusText, data);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return response.json();
}

export const api = {
  get: <T>(endpoint: string, options?: RequestOptions) =>
    request<T>(endpoint, { ...options, method: "GET" }),

  post: <T>(endpoint: string, body?: unknown, options?: RequestOptions) =>
    request<T>(endpoint, { ...options, method: "POST", body }),

  put: <T>(endpoint: string, body?: unknown, options?: RequestOptions) =>
    request<T>(endpoint, { ...options, method: "PUT", body }),

  patch: <T>(endpoint: string, body?: unknown, options?: RequestOptions) =>
    request<T>(endpoint, { ...options, method: "PATCH", body }),

  delete: <T>(endpoint: string, options?: RequestOptions) =>
    request<T>(endpoint, { ...options, method: "DELETE" }),
};

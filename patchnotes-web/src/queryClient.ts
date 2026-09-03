import { QueryClient, QueryCache, MutationCache } from "@tanstack/react-query";
import { isApiError, getErrorMessage } from "./api/client";

type ErrorHandler = (message: string) => void;

let globalErrorHandler: ErrorHandler = (message) => {
  // Fallback: log to console if no handler is registered
  console.error("[API Error]", message);
};

export function setGlobalErrorHandler(handler: ErrorHandler) {
  globalErrorHandler = handler;
}

function handleError(error: unknown) {
  const message = getErrorMessage(error);
  globalErrorHandler(message);
}

function shouldRetry(failureCount: number, error: unknown): boolean {
  // Don't retry on client errors (4xx) except for rate limiting (429)
  if (isApiError(error)) {
    if (error.status >= 400 && error.status < 500 && error.status !== 429) {
      return false;
    }
  }
  // Retry up to 2 times for network/server errors
  return failureCount < 2;
}

export const queryClient = new QueryClient({
  queryCache: new QueryCache({
    onError: (error, query) => {
      // Only show error toast if the query has already been successful before
      // (this avoids showing errors during initial load)
      if (query.state.data !== undefined) {
        handleError(error);
      }
    },
  }),
  mutationCache: new MutationCache({
    onError: (error) => {
      // Always show errors for mutations
      handleError(error);
    },
  }),
  defaultOptions: {
    queries: {
      staleTime: 1000 * 60, // 1 minute
      refetchOnWindowFocus: false,
      retry: shouldRetry,
    },
    mutations: {
      retry: false, // Don't retry mutations by default
    },
  },
});

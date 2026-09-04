import type { FallbackProps } from "react-error-boundary";

export function ErrorFallback({ error, resetErrorBoundary }: FallbackProps) {
  const errorMessage = error instanceof Error ? error.message : "An unknown error occurred";

  return (
    <div className="flex min-h-screen items-center justify-center bg-surface-secondary p-4">
      <div className="max-w-md rounded-lg bg-surface-primary p-6 shadow-lg">
        <h1 className="mb-4 text-xl font-semibold text-text-primary">Something went wrong</h1>
        <p className="mb-4 text-text-secondary">An unexpected error occurred. Please try again.</p>
        <details className="mb-4">
          <summary className="cursor-pointer text-sm text-text-tertiary">Error details</summary>
          <pre className="mt-2 overflow-auto rounded bg-surface-tertiary p-2 text-xs text-major">
            {errorMessage}
          </pre>
        </details>
        <button
          onClick={resetErrorBoundary}
          className="rounded bg-brand-600 px-4 py-2 text-white hover:bg-brand-700"
        >
          Try again
        </button>
      </div>
    </div>
  );
}

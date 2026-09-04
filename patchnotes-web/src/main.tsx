import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { ErrorBoundary } from "react-error-boundary";
import { RouterProvider } from "@tanstack/react-router";
import { QueryClientProvider } from "@tanstack/react-query";
import { StytchProvider } from "@stytch/react";
import { router } from "./router";
import { queryClient } from "./queryClient";
import { stytchClient } from "./auth/stytch";
import { ErrorFallback } from "./components/ErrorFallback";
import { ToastProvider } from "./components/Toast";
import "./index.css";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <ErrorBoundary FallbackComponent={ErrorFallback}>
      <StytchProvider stytch={stytchClient}>
        <ToastProvider>
          <QueryClientProvider client={queryClient}>
            <RouterProvider router={router} />
          </QueryClientProvider>
        </ToastProvider>
      </StytchProvider>
    </ErrorBoundary>
  </StrictMode>,
);

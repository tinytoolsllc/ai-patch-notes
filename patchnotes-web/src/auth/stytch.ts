import { createStytchClient, Products } from "@stytch/react";

const publicToken = import.meta.env.VITE_STYTCH_PUBLIC_TOKEN;

if (!publicToken) {
  console.warn("VITE_STYTCH_PUBLIC_TOKEN is not set. Authentication will not work.");
}

export const stytchClient = createStytchClient(publicToken || "", {
  cookieOptions: {
    domain: import.meta.env.PROD ? ".myreleasenotes.ai" : undefined,
    availableToSubdomains: import.meta.env.PROD,
  },
});

const products = [Products.emailMagicLinks, Products.oauth];

if (import.meta.env.DEV) {
  products.push(Products.passwords);
}

export function getStytchLoginConfig(returnUrl?: string) {
  const base = `${window.location.origin}/authenticate`;
  const redirectURL = returnUrl ? `${base}?returnUrl=${encodeURIComponent(returnUrl)}` : base;

  return {
    products,
    emailMagicLinksOptions: {
      loginRedirectURL: redirectURL,
      loginExpirationMinutes: 30,
      signupRedirectURL: redirectURL,
      signupExpirationMinutes: 30,
    },
    oauthOptions: {
      providers: [
        { type: "github" as const, custom_scopes: ["user:email"] },
        { type: "google" as const },
      ],
      loginRedirectURL: redirectURL,
      signupRedirectURL: redirectURL,
    },
    ...(import.meta.env.DEV && {
      passwordOptions: {
        loginRedirectURL: redirectURL,
        resetPasswordRedirectURL: redirectURL,
      },
    }),
  };
}

// Theme-aware presentation for Stytch login component
export function getStytchPresentation(isDark: boolean) {
  return {
    theme: isDark
      ? {
          "color-scheme": "dark" as const,
          "font-family": '"Inter", ui-sans-serif, system-ui, sans-serif',
          primary: "#818cf8",
          "primary-foreground": "#ffffff",
          background: "transparent",
          foreground: "#e5e7eb",
          secondary: "#374151",
          "secondary-foreground": "#e5e7eb",
          muted: "#1f2937",
          "muted-foreground": "#9ca3af",
          border: "#374151",
          input: "#374151",
          ring: "#818cf8",
          destructive: "#ef4444",
          "destructive-foreground": "#ffffff",
          warning: "#f59e0b",
          success: "#10b981",
          "button-radius": "8px",
          "input-radius": "8px",
          "container-radius": "12px",
        }
      : {
          "color-scheme": "light" as const,
          "font-family": '"Inter", ui-sans-serif, system-ui, sans-serif',
          primary: "#4f46e5",
          "primary-foreground": "#ffffff",
          background: "transparent",
          foreground: "#1f2937",
          secondary: "#f3f4f6",
          "secondary-foreground": "#1f2937",
          muted: "#f9fafb",
          "muted-foreground": "#6b7280",
          border: "#d1d5db",
          input: "#d1d5db",
          ring: "#4f46e5",
          destructive: "#ef4444",
          "destructive-foreground": "#ffffff",
          warning: "#f59e0b",
          success: "#10b981",
          "button-radius": "8px",
          "input-radius": "8px",
          "container-radius": "12px",
        },
  };
}

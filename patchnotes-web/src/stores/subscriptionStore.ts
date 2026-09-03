import { create } from "zustand";
import { api, API_ROOT, isApiError } from "../api/client";

const API_BASE_URL = `${API_ROOT}/api`;

interface SubscriptionState {
  isPro: boolean;
  status: string | null;
  expiresAt: string | null;
  isLoading: boolean;
  error: string | null;
  checkSubscription: () => Promise<void>;
  startCheckout: () => void;
  openPortal: () => void;
  reset: () => void;
}

export const useSubscriptionStore = create<SubscriptionState>((set) => ({
  isPro: false,
  status: null,
  expiresAt: null,
  isLoading: false,
  error: null,

  checkSubscription: async () => {
    set({ isLoading: true, error: null });
    try {
      const { isPro, status, expiresAt } = await api.get<{
        isPro: boolean;
        status: string | null;
        expiresAt: string | null;
      }>("/subscription/status");
      set({ isPro, status, expiresAt, isLoading: false });
    } catch (error) {
      // If unauthorized, user is not logged in - not an error state
      if (isApiError(error) && error.status === 401) {
        set({ isPro: false, status: null, expiresAt: null, isLoading: false });
      } else {
        set({
          error: error instanceof Error ? error.message : "Failed to check subscription",
          isLoading: false,
        });
      }
    }
  },

  startCheckout: () => {
    set({ isLoading: true, error: null });
    // Submit a form POST — the server returns a 303 redirect to Stripe.
    // The redirect URL never touches client-side JS (open redirect prevention).
    const form = document.createElement("form");
    form.method = "POST";
    form.action = `${API_BASE_URL}/subscription/checkout`;
    document.body.appendChild(form);
    form.submit();
    form.remove();
  },

  openPortal: () => {
    set({ isLoading: true, error: null });
    // Submit a form POST — the server returns a 303 redirect to Stripe.
    const form = document.createElement("form");
    form.method = "POST";
    form.action = `${API_BASE_URL}/subscription/portal`;
    document.body.appendChild(form);
    form.submit();
    form.remove();
  },

  reset: () => {
    set({
      isPro: false,
      status: null,
      expiresAt: null,
      isLoading: false,
      error: null,
    });
  },
}));

import { useQuery } from "@tanstack/react-query";
import { API_ROOT } from "../api/client";

interface GeolocationData {
  country_code: string;
}

const ALLOWED_COUNTRIES = ["US", "CA"];

async function fetchGeolocation(): Promise<GeolocationData> {
  if (import.meta.env.DEV && import.meta.env.VITE_BYPASS_GEOFENCING === "true") {
    return { country_code: "US" };
  }

  const response = await fetch(`${API_ROOT}/api/geo/country`);
  if (!response.ok) throw new Error(`HTTP ${response.status}: ${response.statusText}`);
  return response.json();
}

export function useGeofencing() {
  const { data, isLoading, error } = useQuery({
    queryKey: ["geolocation"],
    queryFn: fetchGeolocation,
    staleTime: 24 * 60 * 60 * 1000,
    gcTime: 24 * 60 * 60 * 1000,
    retry: 2,
    retryDelay: (attemptIndex) => Math.min(1000 * 2 ** attemptIndex, 30000),
  });

  return {
    isLoading,
    isAllowed: error ? true : data ? ALLOWED_COUNTRIES.includes(data.country_code) : null,
    error: error?.message || null,
    data: data || null,
  };
}

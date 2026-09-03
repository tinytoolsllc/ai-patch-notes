import { useStytchUser } from "@stytch/react";

interface StytchUserWithRoles {
  roles?: string[];
}

export function useIsAdmin(): { isAdmin: boolean; isLoading: boolean } {
  const { user, isInitialized } = useStytchUser();
  if (!isInitialized) return { isAdmin: false, isLoading: true };
  if (!user) return { isAdmin: false, isLoading: false };
  const roles = (user as unknown as StytchUserWithRoles).roles ?? [];
  return { isAdmin: roles.includes("patch_notes_admin"), isLoading: false };
}

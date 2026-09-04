import { useState, useRef, useEffect } from "react";
import { useStytch, useStytchUser } from "@stytch/react";
import { Link, useNavigate, useLocation } from "@tanstack/react-router";
import { Settings, LogOut, Sparkles, CreditCard, Shield, Sun, Moon } from "lucide-react";
import { useSubscriptionStore } from "../../stores/subscriptionStore";
import { useGeofencing } from "../../hooks/useGeofencing";
import { useIsAdmin } from "../../utils/auth";
import { useTheme } from "../theme/useTheme";

// ============================================================================
// Utilities
// ============================================================================

function getInitials(email?: string, name?: string): string {
  if (name) {
    const parts = name.split(" ");
    if (parts.length >= 2) {
      return `${parts[0][0]}${parts[1][0]}`.toUpperCase();
    }
    return name.slice(0, 2).toUpperCase();
  }
  if (email) {
    const local = email.split("@")[0];
    return local.slice(0, 2).toUpperCase();
  }
  return "?";
}

function getAvatarColor(email?: string): { bg: string; text: string } {
  // Generate consistent color from email
  const colors = [
    { bg: "bg-amber-500", text: "text-amber-50" },
    { bg: "bg-emerald-500", text: "text-emerald-50" },
    { bg: "bg-sky-500", text: "text-sky-50" },
    { bg: "bg-violet-500", text: "text-violet-50" },
    { bg: "bg-rose-500", text: "text-rose-50" },
    { bg: "bg-cyan-500", text: "text-cyan-50" },
    { bg: "bg-orange-500", text: "text-orange-50" },
    { bg: "bg-indigo-500", text: "text-indigo-50" },
  ];

  if (!email) return colors[0];

  let hash = 0;
  for (let i = 0; i < email.length; i++) {
    hash = email.charCodeAt(i) + ((hash << 5) - hash);
  }
  return colors[Math.abs(hash) % colors.length];
}

// ============================================================================
// Avatar Component
// ============================================================================

function Avatar({
  email,
  name,
  size = "md",
}: {
  email?: string;
  name?: string;
  size?: "sm" | "md" | "lg";
}) {
  const initials = getInitials(email, name);
  const { bg, text } = getAvatarColor(email);

  const sizeClasses = {
    sm: "h-7 w-7 text-xs",
    md: "h-9 w-9 text-sm",
    lg: "h-11 w-11 text-base",
  };

  return (
    <div
      className={`${sizeClasses[size]} ${bg} ${text} rounded-full flex items-center justify-center font-semibold select-none`}
    >
      {initials}
    </div>
  );
}

// ============================================================================
// Dropdown Menu
// ============================================================================

function DropdownMenu({
  isOpen,
  onClose,
  email,
  displayName,
  onLogout,
  onSettings,
  isPro,
  onUpgrade,
  onManageSubscription,
  isGeofencingAllowed,
  isAdmin,
  onAdmin,
}: {
  isOpen: boolean;
  onClose: () => void;
  email?: string;
  displayName: string;
  onLogout: () => void;
  onSettings: () => void;
  isPro: boolean;
  onUpgrade: () => void;
  onManageSubscription: () => void;
  isGeofencingAllowed: boolean | null;
  isAdmin: boolean;
  onAdmin: () => void;
}) {
  const menuRef = useRef<HTMLDivElement>(null);

  useEffect(
    function closeMenuOnOutsideInteraction() {
      if (!isOpen) return;

      const handleClickOutside = (e: MouseEvent) => {
        if (menuRef.current && !menuRef.current.contains(e.target as Node)) {
          onClose();
        }
      };

      const handleEscape = (e: KeyboardEvent) => {
        if (e.key === "Escape") onClose();
      };

      document.addEventListener("mousedown", handleClickOutside);
      document.addEventListener("keydown", handleEscape);
      return () => {
        document.removeEventListener("mousedown", handleClickOutside);
        document.removeEventListener("keydown", handleEscape);
      };
    },
    [isOpen, onClose],
  );

  if (!isOpen) return null;

  return (
    <div
      ref={menuRef}
      className="absolute right-0 top-full mt-2 w-64 origin-top-right
        rounded-xl bg-surface-primary border border-border-default
        shadow-lg ring-1 ring-black/5
        animate-in fade-in slide-in-from-top-2 duration-150"
      role="menu"
    >
      {/* User info section */}
      <div className="px-4 py-3 border-b border-border-muted">
        <div className="flex items-center gap-3">
          <Avatar email={email} size="lg" />
          <div className="min-w-0 flex-1">
            <p className="text-sm font-medium text-text-primary truncate">{displayName}</p>
            {email && <p className="text-xs text-text-tertiary truncate">{email}</p>}
          </div>
        </div>
      </div>

      {/* Subscription section */}
      {(isPro || isGeofencingAllowed !== false) && (
        <div className="py-1.5 border-b border-border-muted">
          {isPro ? (
            <MenuButton
              icon={<CreditCard className="w-4 h-4" />}
              onClick={() => {
                onClose();
                onManageSubscription();
              }}
            >
              Manage Subscription
            </MenuButton>
          ) : (
            <MenuButton
              icon={<Sparkles className="w-4 h-4" />}
              onClick={() => {
                onClose();
                onUpgrade();
              }}
              variant="highlight"
            >
              Upgrade to Pro
            </MenuButton>
          )}
        </div>
      )}

      {/* Admin section */}
      {isAdmin && (
        <div className="py-1.5 border-b border-border-muted">
          <MenuButton
            icon={<Shield className="w-4 h-4" />}
            onClick={() => {
              onClose();
              onAdmin();
            }}
          >
            Admin
          </MenuButton>
        </div>
      )}

      {/* Menu items */}
      <div className="py-1.5">
        <MenuButton icon={<Settings className="w-4 h-4" />} onClick={onSettings}>
          Settings
        </MenuButton>
        <ThemeMenuButton />
      </div>

      {/* Sign out section */}
      <div className="border-t border-border-muted py-1.5">
        <MenuButton icon={<LogOut className="w-4 h-4" />} onClick={onLogout} variant="danger">
          Sign out
        </MenuButton>
      </div>
    </div>
  );
}

function ThemeMenuButton() {
  const { resolvedTheme, setTheme } = useTheme();
  const isDark = resolvedTheme === "dark";

  return (
    <MenuButton
      icon={isDark ? <Sun className="w-4 h-4" /> : <Moon className="w-4 h-4" />}
      onClick={() => setTheme(isDark ? "light" : "dark")}
    >
      {isDark ? "Light mode" : "Dark mode"}
    </MenuButton>
  );
}

function MenuButton({
  children,
  icon,
  onClick,
  variant = "default",
}: {
  children: React.ReactNode;
  icon: React.ReactNode;
  onClick: () => void;
  variant?: "default" | "danger" | "highlight";
}) {
  const variantClasses = {
    default: "text-text-primary hover:bg-surface-tertiary",
    danger: "text-red-600 hover:bg-red-50 dark:text-red-400 dark:hover:bg-red-950/30",
    highlight:
      "text-brand-600 hover:bg-brand-50 dark:text-brand-400 dark:hover:bg-brand-950/30 font-medium",
  };

  return (
    <button
      onClick={onClick}
      className={`w-full flex items-center gap-3 px-4 py-2 text-sm transition-colors ${variantClasses[variant]}`}
      role="menuitem"
    >
      <span className={`w-4 h-4 ${variant === "highlight" ? "" : "opacity-60"}`}>{icon}</span>
      {children}
    </button>
  );
}

// ============================================================================
// Main Component
// ============================================================================

export function UserMenu() {
  const stytch = useStytch();
  const { user, isInitialized } = useStytchUser();
  const [isOpen, setIsOpen] = useState(false);
  const navigate = useNavigate();
  const { isPro, checkSubscription, startCheckout, openPortal } = useSubscriptionStore();
  const { isAllowed: isGeofencingAllowed } = useGeofencing();
  const { isAdmin } = useIsAdmin();
  const location = useLocation();

  // Check subscription status when user is available
  useEffect(
    function checkSubscriptionOnLogin() {
      if (user) {
        void checkSubscription();
      }
    },
    [user, checkSubscription],
  );

  const handleLogout = async () => {
    setIsOpen(false);
    await stytch.session.revoke();
  };

  const handleSettings = () => {
    setIsOpen(false);
    void navigate({ to: "/settings" });
  };

  const handleUpgrade = () => {
    startCheckout();
  };

  const handleManageSubscription = () => {
    openPortal();
  };

  const handleAdmin = () => {
    void navigate({ to: "/admin" });
  };

  // Loading state
  if (!isInitialized) {
    return <div className="h-9 w-9 animate-pulse rounded-full bg-surface-tertiary" />;
  }

  // Signed out state
  if (!user) {
    return (
      <Link
        to="/login"
        search={{ returnUrl: location.pathname }}
        className="flex items-center gap-2 rounded-lg px-3 py-2 text-sm font-medium cursor-default
          text-text-primary bg-surface-primary border border-border-default
          hover:border-border-default hover:bg-surface-tertiary
          transition-colors duration-150"
      >
        Sign in
      </Link>
    );
  }

  // Signed in state
  const email = user.emails?.[0]?.email;
  const displayName = user.name?.first_name || email?.split("@")[0] || "User";

  return (
    <div className="relative">
      <button
        onClick={() => setIsOpen(!isOpen)}
        className="flex items-center rounded-full
          ring-2 ring-transparent hover:ring-border-default
          focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500
          transition-all duration-150"
        aria-expanded={isOpen}
        aria-haspopup="true"
      >
        <Avatar email={email} />
      </button>

      <DropdownMenu
        isOpen={isOpen}
        onClose={() => setIsOpen(false)}
        email={email}
        displayName={displayName}
        onLogout={handleLogout}
        onSettings={handleSettings}
        isPro={isPro}
        onUpgrade={handleUpgrade}
        onManageSubscription={handleManageSubscription}
        isGeofencingAllowed={isGeofencingAllowed}
        isAdmin={isAdmin}
        onAdmin={handleAdmin}
      />
    </div>
  );
}

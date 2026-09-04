import { Sun, Moon } from "lucide-react";
import { useTheme } from "./useTheme";
import { Tooltip } from "../ui";

export function ThemeToggle() {
  const { resolvedTheme, setTheme } = useTheme();

  const handleToggle = () => {
    setTheme(resolvedTheme === "light" ? "dark" : "light");
  };

  const label = `Switch to ${resolvedTheme === "light" ? "dark" : "light"} mode`;

  return (
    <Tooltip label={label}>
      <button
        onClick={handleToggle}
        className="relative flex h-9 w-9 items-center justify-center rounded-lg
          text-text-secondary hover:text-text-primary hover:bg-surface-tertiary
          transition-all duration-200 focus-visible:outline-none focus-visible:ring-2
          focus-visible:ring-brand-500 focus-visible:ring-offset-2 focus-visible:ring-offset-surface-primary"
        aria-label={label}
      >
        <Sun
          className={`absolute h-5 w-5 transition-all duration-300 ${
            resolvedTheme === "light"
              ? "rotate-0 scale-100 opacity-100"
              : "rotate-90 scale-0 opacity-0"
          }`}
          strokeWidth={1.5}
        />

        <Moon
          className={`absolute h-5 w-5 transition-all duration-300 ${
            resolvedTheme === "dark"
              ? "rotate-0 scale-100 opacity-100"
              : "-rotate-90 scale-0 opacity-0"
          }`}
          strokeWidth={1.5}
        />
      </button>
    </Tooltip>
  );
}

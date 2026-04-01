import { useEffect } from 'react'
import { useThemeStore, getSystemTheme, applyTheme } from './themeStore'

// Hook that handles system theme changes
export function useTheme() {
  const { theme, resolvedTheme, setTheme, setResolvedTheme } = useThemeStore()

  useEffect(
    function syncThemeWithSystem() {
      // Apply theme on mount
      applyTheme(resolvedTheme)

      // Listen for system theme changes
      const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)')
      const handleChange = () => {
        if (useThemeStore.getState().theme === 'system') {
          const newResolved = getSystemTheme()
          setResolvedTheme(newResolved)
        }
      }

      mediaQuery.addEventListener('change', handleChange)
      return () => mediaQuery.removeEventListener('change', handleChange)
    },
    [resolvedTheme, setResolvedTheme]
  )

  return { theme, resolvedTheme, setTheme }
}

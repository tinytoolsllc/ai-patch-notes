/* eslint-disable react-refresh/only-export-components */
import {
  createContext,
  useContext,
  useState,
  useCallback,
  useEffect,
  useRef,
  type ReactNode,
} from 'react'
import { setGlobalErrorHandler } from '../queryClient'

type ToastType = 'error' | 'success' | 'info'

interface Toast {
  id: string
  message: string
  type: ToastType
}

interface ToastContextValue {
  showToast: (message: string, type?: ToastType) => void
  showError: (message: string) => void
}

const ToastContext = createContext<ToastContextValue | null>(null)

export function useToast(): ToastContextValue {
  const context = useContext(ToastContext)
  if (!context) {
    throw new Error('useToast must be used within a ToastProvider')
  }
  return context
}

const TOAST_DURATION = 5000

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([])
  const timeoutRefs = useRef<Map<string, ReturnType<typeof setTimeout>>>(
    new Map()
  )

  const removeToast = useCallback((id: string) => {
    const timeout = timeoutRefs.current.get(id)
    if (timeout) {
      clearTimeout(timeout)
      timeoutRefs.current.delete(id)
    }
    setToasts((prev) => prev.filter((t) => t.id !== id))
  }, [])

  const showToast = useCallback(
    (message: string, type: ToastType = 'info') => {
      const id = `${Date.now()}-${Math.random().toString(36).slice(2, 9)}`
      setToasts((prev) => [...prev, { id, message, type }])
      const timeout = setTimeout(() => {
        timeoutRefs.current.delete(id)
        removeToast(id)
      }, TOAST_DURATION)
      timeoutRefs.current.set(id, timeout)
    },
    [removeToast]
  )

  useEffect(() => {
    const timeouts = timeoutRefs.current
    return () => {
      timeouts.forEach((t) => clearTimeout(t))
    }
  }, [])

  const showError = useCallback(
    (message: string) => {
      showToast(message, 'error')
    },
    [showToast]
  )

  useEffect(() => {
    setGlobalErrorHandler(showError)
  }, [showError])

  return (
    <ToastContext value={{ showToast, showError }}>
      {children}
      <ToastContainer toasts={toasts} onDismiss={removeToast} />
    </ToastContext>
  )
}

function ToastContainer({
  toasts,
  onDismiss,
}: {
  toasts: Toast[]
  onDismiss: (id: string) => void
}) {
  if (toasts.length === 0) return null

  return (
    <div className="fixed bottom-4 right-4 z-50 flex flex-col gap-2">
      {toasts.map((toast) => (
        <ToastItem key={toast.id} toast={toast} onDismiss={onDismiss} />
      ))}
    </div>
  )
}

function ToastItem({
  toast,
  onDismiss,
}: {
  toast: Toast
  onDismiss: (id: string) => void
}) {
  const bgColor = {
    error: 'bg-red-600',
    success: 'bg-green-600',
    info: 'bg-blue-600',
  }[toast.type]

  return (
    <div
      className={`${bgColor} flex max-w-sm items-center gap-3 rounded-lg px-4 py-3 text-white shadow-lg`}
      role="alert"
    >
      <p className="flex-1 text-sm">{toast.message}</p>
      <button
        onClick={() => onDismiss(toast.id)}
        className="text-white/80 hover:text-white"
        aria-label="Dismiss"
      >
        <svg
          className="h-4 w-4"
          fill="none"
          stroke="currentColor"
          viewBox="0 0 24 24"
        >
          <path
            strokeLinecap="round"
            strokeLinejoin="round"
            strokeWidth={2}
            d="M6 18L18 6M6 6l12 12"
          />
        </svg>
      </button>
    </div>
  )
}

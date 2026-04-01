import { type HTMLAttributes, forwardRef, useEffect } from 'react'
import { createPortal } from 'react-dom'
import { Button } from './Button'

interface ModalProps extends HTMLAttributes<HTMLDivElement> {
  open: boolean
  onClose: () => void
  title?: string
}

export const Modal = forwardRef<HTMLDivElement, ModalProps>(
  ({ open, onClose, title, className = '', children, ...props }, ref) => {
    useEffect(
      function lockBodyScroll() {
        if (open) {
          document.body.style.overflow = 'hidden'
        } else {
          document.body.style.overflow = ''
        }
        return () => {
          document.body.style.overflow = ''
        }
      },
      [open]
    )

    useEffect(
      function closeOnEscapeKey() {
        const handleEscape = (e: KeyboardEvent) => {
          if (e.key === 'Escape' && open) {
            onClose()
          }
        }
        document.addEventListener('keydown', handleEscape)
        return () => document.removeEventListener('keydown', handleEscape)
      },
      [open, onClose]
    )

    if (!open) return null

    return createPortal(
      <div className="fixed inset-0 z-50 flex items-center justify-center">
        <div
          className="absolute inset-0 bg-black/50 backdrop-blur-sm"
          onClick={onClose}
        />
        <div
          ref={ref}
          role="dialog"
          aria-modal="true"
          aria-labelledby={title ? 'modal-title' : undefined}
          className={`
            relative z-10 w-full max-w-md mx-4
            bg-surface-primary border border-border-default
            rounded-xl shadow-xl
            ${className}
          `}
          {...props}
        >
          {title && (
            <div className="flex items-center justify-between px-6 py-4 border-b border-border-default">
              <h2
                id="modal-title"
                className="text-lg font-semibold text-text-primary"
              >
                {title}
              </h2>
              <Button
                variant="ghost"
                size="sm"
                onClick={onClose}
                aria-label="Close"
              >
                <svg
                  className="w-5 h-5"
                  viewBox="0 0 20 20"
                  fill="currentColor"
                >
                  <path
                    fillRule="evenodd"
                    d="M4.293 4.293a1 1 0 011.414 0L10 8.586l4.293-4.293a1 1 0 111.414 1.414L11.414 10l4.293 4.293a1 1 0 01-1.414 1.414L10 11.414l-4.293 4.293a1 1 0 01-1.414-1.414L8.586 10 4.293 5.707a1 1 0 010-1.414z"
                    clipRule="evenodd"
                  />
                </svg>
              </Button>
            </div>
          )}
          <div className="p-6">{children}</div>
        </div>
      </div>,
      document.body
    )
  }
)

Modal.displayName = 'Modal'

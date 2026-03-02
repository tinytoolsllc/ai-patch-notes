import { render, screen } from '../test/utils'
import { render as rawRender } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ToastProvider, useToast } from './Toast'
import { act } from 'react'

function TestComponent() {
  const { showToast, showError } = useToast()

  return (
    <div>
      <button onClick={() => showToast('Info message')}>Show Info</button>
      <button onClick={() => showToast('Success message', 'success')}>
        Show Success
      </button>
      <button onClick={() => showError('Error message')}>Show Error</button>
    </div>
  )
}

describe('Toast', () => {
  describe('ToastProvider', () => {
    it('renders children', () => {
      render(
        <ToastProvider>
          <div>Test content</div>
        </ToastProvider>
      )

      expect(screen.getByText('Test content')).toBeInTheDocument()
    })
  })

  describe('useToast', () => {
    it('throws when used outside provider', () => {
      const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => {})

      expect(() => {
        rawRender(<TestComponent />)
      }).toThrow('useToast must be used within a ToastProvider')

      consoleSpy.mockRestore()
    })
  })

  describe('showToast', () => {
    it('shows info toast by default', async () => {
      const user = userEvent.setup()
      render(
        <ToastProvider>
          <TestComponent />
        </ToastProvider>
      )

      await user.click(screen.getByText('Show Info'))

      expect(screen.getByRole('alert')).toBeInTheDocument()
      expect(screen.getByText('Info message')).toBeInTheDocument()
    })

    it('shows success toast', async () => {
      const user = userEvent.setup()
      render(
        <ToastProvider>
          <TestComponent />
        </ToastProvider>
      )

      await user.click(screen.getByText('Show Success'))

      const toast = screen.getByRole('alert')
      expect(toast).toBeInTheDocument()
      expect(toast.className).toContain('bg-green-600')
    })

    it('shows error toast using showError', async () => {
      const user = userEvent.setup()
      render(
        <ToastProvider>
          <TestComponent />
        </ToastProvider>
      )

      await user.click(screen.getByText('Show Error'))

      const toast = screen.getByRole('alert')
      expect(toast).toBeInTheDocument()
      expect(toast.className).toContain('bg-red-600')
      expect(screen.getByText('Error message')).toBeInTheDocument()
    })

    it('auto-dismisses after timeout', async () => {
      vi.useFakeTimers()

      render(
        <ToastProvider>
          <TestComponent />
        </ToastProvider>
      )

      // Click directly on the button element
      const button = screen.getByText('Show Info')
      await act(async () => {
        button.click()
      })

      expect(screen.getByRole('alert')).toBeInTheDocument()

      // Advance past the toast duration
      await act(async () => {
        vi.advanceTimersByTime(5001)
      })

      expect(screen.queryByRole('alert')).not.toBeInTheDocument()

      vi.useRealTimers()
    })

    it('can be manually dismissed', async () => {
      vi.useFakeTimers()

      render(
        <ToastProvider>
          <TestComponent />
        </ToastProvider>
      )

      await act(async () => {
        screen.getByText('Show Info').click()
      })

      expect(screen.getByRole('alert')).toBeInTheDocument()

      await act(async () => {
        screen.getByRole('button', { name: 'Dismiss' }).click()
      })

      expect(screen.queryByRole('alert')).not.toBeInTheDocument()

      vi.useRealTimers()
    })

    it('can show multiple toasts', async () => {
      vi.useFakeTimers()

      render(
        <ToastProvider>
          <TestComponent />
        </ToastProvider>
      )

      await act(async () => {
        screen.getByText('Show Info').click()
      })
      await act(async () => {
        screen.getByText('Show Success').click()
      })
      await act(async () => {
        screen.getByText('Show Error').click()
      })

      const toasts = screen.getAllByRole('alert')
      expect(toasts).toHaveLength(3)

      vi.useRealTimers()
    })
  })
})

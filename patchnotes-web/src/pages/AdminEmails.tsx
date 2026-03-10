import { useState, useEffect, useMemo, useRef } from 'react'
import { useNavigate } from '@tanstack/react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import {
  AppHeader,
  Breadcrumb,
  Container,
  Button,
  Card,
} from '../components/ui'
import { useStytchUser } from '@stytch/react'
import { useIsAdmin } from '../utils/auth'
import { api } from '../api/client'
import {
  useUpdateEmailTemplate,
  getGetEmailTemplatesQueryKey,
} from '../api/hooks'

// ── Types ────────────────────────────────────────────────────

interface EmailTemplateDto {
  id: string
  name: string
  subject: string
  jsxSource: string
  updatedAt: string
}

// ── Sample Data ──────────────────────────────────────────────

const EMPTY_SAMPLE_DATA: Record<string, unknown> = {}

const SAMPLE_DATA: Record<string, Record<string, unknown>> = {
  welcome: { name: 'Jane Doe' },
  digest: {
    name: 'Jane Doe',
    count: '2',
    packages: [
      {
        packageName: 'react',
        releaseCount: 3,
        latestVersion: '19.1.0',
        oldestVersion: '19.0.1',
        summary: 'New compiler features and performance improvements',
      },
      {
        packageName: 'lodash',
        releaseCount: 1,
        latestVersion: '5.0.0',
        oldestVersion: '5.0.0',
        summary: 'ES module support and tree-shaking improvements',
      },
    ],
  },
}

// ── Template Tab ─────────────────────────────────────────────

function TemplateTab({
  label,
  active,
  onClick,
}: {
  label: string
  active: boolean
  onClick: () => void
}) {
  return (
    <button
      onClick={onClick}
      className={`px-4 py-2 text-sm font-medium rounded-lg transition-colors ${
        active
          ? 'bg-brand-500 text-white'
          : 'bg-surface-primary text-text-secondary hover:text-text-primary hover:bg-surface-secondary'
      }`}
    >
      {label}
    </button>
  )
}

// ── Main Page ────────────────────────────────────────────────

export function AdminEmails() {
  const { isAdmin, isLoading: authLoading } = useIsAdmin()
  const navigate = useNavigate()
  const [selectedTemplateId, setSelectedTemplateId] = useState<string | null>(
    null
  )
  const [showSource, setShowSource] = useState(false)
  const [isEditing, setIsEditing] = useState(false)
  const [editSubject, setEditSubject] = useState('')
  const [editJsxSource, setEditJsxSource] = useState('')
  const [saveStatus, setSaveStatus] = useState<{
    type: 'success' | 'error'
    message: string
  } | null>(null)

  const [showTestEmailCard, setShowTestEmailCard] = useState(false)
  const [testEmailAddress, setTestEmailAddress] = useState('')
  const [useSampleData, setUseSampleData] = useState(false)
  const [testSendStatus, setTestSendStatus] = useState<{
    type: 'sending' | 'success' | 'error'
    message: string
  } | null>(null)

  const { user: stytchUser } = useStytchUser()

  const statusTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const testStatusTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(
    null
  )
  const clearStatusTimeout = () => {
    if (statusTimeoutRef.current) {
      clearTimeout(statusTimeoutRef.current)
      statusTimeoutRef.current = null
    }
  }
  const clearTestStatusTimeout = () => {
    if (testStatusTimeoutRef.current) {
      clearTimeout(testStatusTimeoutRef.current)
      testStatusTimeoutRef.current = null
    }
  }

  useEffect(() => {
    return () => {
      clearStatusTimeout()
      clearTestStatusTimeout()
    }
  }, [])

  const queryClient = useQueryClient()
  const updateMutation = useUpdateEmailTemplate({
    mutation: {
      onSuccess: () => {
        queryClient.invalidateQueries({
          queryKey: getGetEmailTemplatesQueryKey(),
        })
        setIsEditing(false)
        setSaveStatus({
          type: 'success',
          message: 'Template saved successfully',
        })
        clearStatusTimeout()
        statusTimeoutRef.current = setTimeout(() => setSaveStatus(null), 3000)
      },
      onError: () => {
        setSaveStatus({ type: 'error', message: 'Failed to save template' })
        clearStatusTimeout()
        statusTimeoutRef.current = setTimeout(() => setSaveStatus(null), 5000)
      },
    },
  })

  const { data: templates, isLoading } = useQuery({
    queryKey: ['/api/admin/email-templates'],
    queryFn: () => api.get<EmailTemplateDto[]>('/admin/email-templates'),
    enabled: isAdmin,
  })

  // Auth gate: redirect non-admins
  useEffect(() => {
    if (!authLoading && !isAdmin) {
      navigate({ to: '/' })
    }
  }, [authLoading, isAdmin, navigate])

  const activeTemplateId = selectedTemplateId ?? templates?.[0]?.id ?? ''
  const currentTemplate = templates?.find((t) => t.id === activeTemplateId)
  const sampleData =
    SAMPLE_DATA[currentTemplate?.name ?? ''] ?? EMPTY_SAMPLE_DATA

  const {
    data: previewData,
    isLoading: previewLoading,
    error: previewError,
  } = useQuery({
    queryKey: [
      '/api/admin/email-templates/preview',
      currentTemplate?.jsxSource,
      activeTemplateId,
      sampleData,
    ],
    queryFn: () =>
      api.post<{ html: string }>('/admin/email-templates/preview', {
        jsxSource: currentTemplate!.jsxSource,
        props: sampleData,
      }),
    enabled: !!currentTemplate && isAdmin,
  })
  const previewHtml = previewData?.html ?? ''

  const subjectPreview = useMemo(() => {
    if (!currentTemplate) return ''
    return currentTemplate.subject.replace(/\{\{(\w+)\}\}/g, (_, key: string) =>
      String(sampleData[key] ?? `{{${key}}}`)
    )
  }, [currentTemplate, sampleData])

  function enterEditMode() {
    if (!currentTemplate) return
    setEditSubject(currentTemplate.subject)
    setEditJsxSource(currentTemplate.jsxSource)
    setIsEditing(true)
    setShowSource(true)
    setSaveStatus(null)
  }

  function cancelEdit() {
    setIsEditing(false)
    setSaveStatus(null)
  }

  function handleSave() {
    if (!currentTemplate) return
    updateMutation.mutate({
      id: currentTemplate.id,
      data: {
        subject: editSubject,
        jsxSource: editJsxSource,
      },
    })
  }

  async function handleSendTestEmail() {
    if (!currentTemplate || !testEmailAddress) return

    setTestSendStatus({ type: 'sending', message: 'Sending test email...' })

    try {
      const payload: Record<string, unknown> = {
        recipientEmail: testEmailAddress,
      }
      if (useSampleData) {
        payload.testData = sampleData
      }
      await api.post(
        `/admin/email-templates/${currentTemplate.id}/test`,
        payload
      )
      setTestSendStatus({
        type: 'success',
        message: `Test email sent to ${testEmailAddress}`,
      })
      clearTestStatusTimeout()
      testStatusTimeoutRef.current = setTimeout(() => {
        setTestSendStatus(null)
        setShowTestEmailCard(false)
      }, 3000)
    } catch {
      setTestSendStatus({
        type: 'error',
        message: 'Failed to send test email',
      })
      clearTestStatusTimeout()
      testStatusTimeoutRef.current = setTimeout(
        () => setTestSendStatus(null),
        5000
      )
    }
  }

  if (authLoading || !isAdmin) {
    return (
      <div className="min-h-screen bg-surface-secondary flex items-center justify-center">
        <p className="text-text-secondary">
          {authLoading ? 'Checking access...' : 'Redirecting...'}
        </p>
      </div>
    )
  }

  return (
    <div className="min-h-screen bg-surface-secondary">
      <AppHeader breadcrumbs={<Breadcrumb>Admin</Breadcrumb>} />

      <main className="py-8">
        <Container>
          {/* Template Selector */}
          <div className="flex gap-2 mb-6">
            {(templates ?? []).map((t) => {
              const name = t.name || `Template #${t.id}`
              return (
                <TemplateTab
                  key={t.id}
                  label={name.charAt(0).toUpperCase() + name.slice(1)}
                  active={activeTemplateId === t.id}
                  onClick={() => {
                    setSelectedTemplateId(t.id)
                    setIsEditing(false)
                    setSaveStatus(null)
                  }}
                />
              )
            })}
            {isLoading && (
              <span className="text-text-secondary text-sm py-2">
                Loading templates...
              </span>
            )}
          </div>

          {currentTemplate && (
            <div className="space-y-6">
              {/* Subject Line */}
              <Card>
                <div className="space-y-2">
                  <label className="text-xs font-medium text-text-secondary uppercase tracking-wide">
                    Subject Line
                  </label>
                  {isEditing ? (
                    <input
                      type="text"
                      value={editSubject}
                      onChange={(e) => setEditSubject(e.target.value)}
                      className="w-full px-3 py-2 text-sm font-mono text-text-primary bg-surface-secondary border border-border-default rounded-lg focus:outline-none focus:ring-2 focus:ring-brand-500"
                    />
                  ) : (
                    <>
                      <p className="text-text-primary font-medium">
                        {subjectPreview}
                      </p>
                      <p className="text-xs text-text-tertiary">
                        Template: {currentTemplate.subject}
                      </p>
                    </>
                  )}
                </div>
              </Card>

              {/* Toggle Source / Preview + Edit controls */}
              <div className="flex items-center gap-2">
                <Button
                  variant={showSource ? 'secondary' : 'primary'}
                  size="sm"
                  onClick={() => setShowSource(false)}
                  disabled={isEditing}
                >
                  Preview
                </Button>
                <Button
                  variant={showSource ? 'primary' : 'secondary'}
                  size="sm"
                  onClick={() => setShowSource(true)}
                >
                  JSX Source
                </Button>
                <div className="ml-auto flex items-center gap-2">
                  {saveStatus && (
                    <span
                      className={`text-sm ${saveStatus.type === 'success' ? 'text-green-600' : 'text-red-600'}`}
                    >
                      {saveStatus.message}
                    </span>
                  )}
                  {isEditing ? (
                    <>
                      <Button
                        variant="secondary"
                        size="sm"
                        onClick={cancelEdit}
                        disabled={updateMutation.isPending}
                      >
                        Cancel
                      </Button>
                      <Button
                        variant="primary"
                        size="sm"
                        onClick={handleSave}
                        disabled={updateMutation.isPending}
                      >
                        {updateMutation.isPending ? 'Saving...' : 'Save'}
                      </Button>
                    </>
                  ) : (
                    <>
                      <Button
                        variant="secondary"
                        size="sm"
                        onClick={() => {
                          const email = stytchUser?.emails?.[0]?.email ?? ''
                          setTestEmailAddress(email)
                          setShowTestEmailCard(true)
                          setTestSendStatus(null)
                        }}
                      >
                        Send Test Email
                      </Button>
                      <Button
                        variant="secondary"
                        size="sm"
                        onClick={enterEditMode}
                      >
                        Edit
                      </Button>
                    </>
                  )}
                </div>
              </div>

              {showTestEmailCard && !isEditing && (
                <Card>
                  <div className="space-y-3">
                    <h3 className="text-sm font-semibold text-text-primary">
                      Send Test Email
                    </h3>
                    <p className="text-xs text-text-secondary">
                      Sends a real email using the{' '}
                      <strong>
                        {currentTemplate?.name ||
                          `Template #${currentTemplate?.id}`}
                      </strong>{' '}
                      template
                      {useSampleData
                        ? ' with the sample data shown below.'
                        : ' with real data from the database.'}
                    </p>
                    <div>
                      <label className="text-xs font-medium text-text-secondary">
                        Recipient Email
                      </label>
                      <input
                        type="email"
                        value={testEmailAddress}
                        onChange={(e) => setTestEmailAddress(e.target.value)}
                        placeholder="admin@example.com"
                        className="w-full mt-1 px-3 py-2 text-sm text-text-primary bg-surface-secondary border border-border-default rounded-lg focus:outline-none focus:ring-2 focus:ring-brand-500"
                      />
                    </div>
                    <label className="flex items-center gap-2 text-sm text-text-secondary cursor-pointer">
                      <input
                        type="checkbox"
                        checked={useSampleData}
                        onChange={(e) => setUseSampleData(e.target.checked)}
                        className="rounded border-border-default"
                      />
                      Use sample data instead of real database data
                    </label>
                    <div className="flex items-center gap-2">
                      <Button
                        variant="primary"
                        size="sm"
                        onClick={handleSendTestEmail}
                        disabled={
                          !testEmailAddress ||
                          testSendStatus?.type === 'sending'
                        }
                      >
                        {testSendStatus?.type === 'sending'
                          ? 'Sending...'
                          : 'Send'}
                      </Button>
                      <Button
                        variant="secondary"
                        size="sm"
                        onClick={() => {
                          setShowTestEmailCard(false)
                          setTestSendStatus(null)
                        }}
                      >
                        Cancel
                      </Button>
                      {testSendStatus && testSendStatus.type !== 'sending' && (
                        <span
                          className={`text-sm ${
                            testSendStatus.type === 'success'
                              ? 'text-green-600'
                              : 'text-red-600'
                          }`}
                        >
                          {testSendStatus.message}
                        </span>
                      )}
                    </div>
                  </div>
                </Card>
              )}

              {showSource ? (
                /* JSX Source Code */
                <Card padding="none">
                  <div className="px-6 py-4 border-b border-border-default">
                    <h2 className="text-sm font-semibold text-text-primary">
                      JSX Source —{' '}
                      {currentTemplate.name ||
                        `Template #${currentTemplate.id}`}
                    </h2>
                    <p className="text-xs text-text-tertiary mt-1">
                      Last updated:{' '}
                      {new Date(currentTemplate.updatedAt).toLocaleString()}
                    </p>
                  </div>
                  {isEditing ? (
                    <textarea
                      value={editJsxSource}
                      onChange={(e) => setEditJsxSource(e.target.value)}
                      className="w-full p-6 text-sm font-mono text-text-primary bg-surface-primary leading-relaxed border-0 resize-y focus:outline-none focus:ring-2 focus:ring-inset focus:ring-brand-500"
                      style={{ minHeight: '400px' }}
                      spellCheck={false}
                    />
                  ) : (
                    <pre className="p-6 overflow-x-auto text-sm font-mono text-text-primary bg-surface-primary leading-relaxed">
                      <code>{currentTemplate.jsxSource}</code>
                    </pre>
                  )}
                </Card>
              ) : (
                /* Rendered HTML Preview */
                <Card padding="none">
                  <div className="px-6 py-4 border-b border-border-default">
                    <h2 className="text-sm font-semibold text-text-primary">
                      Email Preview —{' '}
                      {currentTemplate.name ||
                        `Template #${currentTemplate.id}`}
                    </h2>
                    <p className="text-xs text-text-tertiary mt-1">
                      Rendered with sample data
                    </p>
                  </div>
                  <div className="p-4 bg-neutral-100">
                    {previewLoading ? (
                      <div className="flex items-center justify-center py-16">
                        <span className="text-text-secondary text-sm">
                          Rendering preview...
                        </span>
                      </div>
                    ) : previewError ? (
                      <div className="flex items-center justify-center py-16">
                        <span className="text-red-600 text-sm">
                          Failed to render preview
                        </span>
                      </div>
                    ) : (
                      <iframe
                        srcDoc={previewHtml}
                        title={`Preview of ${currentTemplate.name || `Template #${currentTemplate.id}`} template`}
                        className="w-full border-0 rounded bg-white"
                        style={{ minHeight: '400px' }}
                        sandbox=""
                      />
                    )}
                  </div>
                </Card>
              )}

              {/* Sample Data */}
              <Card>
                <div className="space-y-2">
                  <label className="text-xs font-medium text-text-secondary uppercase tracking-wide">
                    Sample Data
                  </label>
                  <pre className="text-sm font-mono text-text-secondary bg-surface-secondary rounded-lg p-4 overflow-x-auto">
                    {JSON.stringify(sampleData, null, 2)}
                  </pre>
                </div>
              </Card>
            </div>
          )}
        </Container>
      </main>
    </div>
  )
}

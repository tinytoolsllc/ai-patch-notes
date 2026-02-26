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
import { useIsAdmin } from '../utils/auth'
import { api } from '../api/client'
import {
  useUpdateEmailTemplate,
  getGetEmailTemplatesQueryKey,
} from '../api/generated/email-templates/email-templates'

// ── Types ────────────────────────────────────────────────────

interface EmailTemplateDto {
  id: string
  name: string
  subject: string
  jsxSource: string
  updatedAt: string
}

// ── Sample Data ──────────────────────────────────────────────

const SAMPLE_DATA: Record<string, Record<string, unknown>> = {
  welcome: { name: 'Jane Doe' },
  digest: {
    name: 'Jane Doe',
    releases: [
      {
        packageName: 'react',
        version: '19.1.0',
        summary: 'New compiler features and performance improvements',
      },
      {
        packageName: 'lodash',
        version: '5.0.0',
        summary: 'ES module support and tree-shaking improvements',
      },
    ],
  },
}

// ── Preview HTML Generator ───────────────────────────────────

function generatePreviewHtml(
  template: EmailTemplateDto,
  sampleData: Record<string, unknown>
): string {
  const subject = template.subject.replace(/\{\{(\w+)\}\}/g, (_, key) =>
    String(sampleData[key] ?? `{{${key}}}`)
  )

  // Generate a simple HTML preview from the template name and sample data
  switch (template.name) {
    case 'welcome': {
      return `<!DOCTYPE html>
<html><head><meta charset="utf-8"></head>
<body style="background-color:#f6f9fc;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;margin:0;padding:20px">
  <div style="background-color:#ffffff;margin:0 auto;padding:40px 20px;max-width:560px;border-radius:8px">
    <h1 style="color:#1a1a1a;font-size:24px;font-weight:bold;margin:0 0 16px">${subject}</h1>
    <p style="color:#4a4a4a;font-size:16px;line-height:26px">
      You're all set to receive release notifications for the packages you care about.
    </p>
    <p style="color:#4a4a4a;font-size:16px;line-height:26px">
      Head to your <a href="#" style="color:#5469d4">dashboard</a> to start watching packages.
    </p>
    <hr style="border:none;border-top:1px solid #e6ebf1;margin:32px 0" />
    <p style="color:#8898aa;font-size:12px">PatchNotes — Release notifications for developers</p>
  </div>
</body></html>`
    }
    case 'digest': {
      const name = String(sampleData.name ?? 'there')
      const releases =
        (sampleData.releases as Array<{
          packageName: string
          version: string
          summary: string
        }>) ?? []
      const releaseList = releases
        .map(
          (r) =>
            `<li style="color:#4a4a4a;font-size:16px;line-height:26px;margin-bottom:8px"><strong>${r.packageName} ${r.version}</strong>: ${r.summary}</li>`
        )
        .join('\n')
      return `<!DOCTYPE html>
<html><head><meta charset="utf-8"></head>
<body style="background-color:#f6f9fc;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;margin:0;padding:20px">
  <div style="background-color:#ffffff;margin:0 auto;padding:40px 20px;max-width:560px;border-radius:8px">
    <h1 style="color:#1a1a1a;font-size:24px;font-weight:bold;margin:0 0 16px">Your Weekly PatchNotes Digest</h1>
    <p style="color:#4a4a4a;font-size:16px;line-height:26px">Hi ${name}, here's what happened this week with the packages you're watching:</p>
    <ul style="padding:0 0 0 20px">${releaseList}</ul>
    <p style="color:#4a4a4a;font-size:16px;line-height:26px">
      <a href="#" style="color:#5469d4">View all updates on PatchNotes</a>
    </p>
    <hr style="border:none;border-top:1px solid #e6ebf1;margin:32px 0" />
    <p style="color:#8898aa;font-size:12px">PatchNotes — Release notifications for developers</p>
  </div>
</body></html>`
    }
    default:
      return `<html><body><p>No preview available for template "${template.name}"</p></body></html>`
  }
}

// ── Template Tab ─────────────────────────────────────────────

interface TemplateTabProps {
  name: string
  active: boolean
  onClick: () => void
}

function TemplateTab({ name, active, onClick }: TemplateTabProps) {
  const label = name.charAt(0).toUpperCase() + name.slice(1)
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
  const [selectedTemplate, setSelectedTemplate] = useState<string>('welcome')
  const [showSource, setShowSource] = useState(false)
  const [isEditing, setIsEditing] = useState(false)
  const [editSubject, setEditSubject] = useState('')
  const [editJsxSource, setEditJsxSource] = useState('')
  const [saveStatus, setSaveStatus] = useState<{
    type: 'success' | 'error'
    message: string
  } | null>(null)

  const statusTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  function clearStatusTimeout() {
    if (statusTimeoutRef.current) {
      clearTimeout(statusTimeoutRef.current)
      statusTimeoutRef.current = null
    }
  }

  useEffect(() => {
    return () => clearStatusTimeout()
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

  const currentTemplate = templates?.find((t) => t.name === selectedTemplate)
  const sampleData = useMemo(
    () => SAMPLE_DATA[selectedTemplate] ?? {},
    [selectedTemplate]
  )

  const previewHtml = useMemo(() => {
    if (!currentTemplate) return ''
    return generatePreviewHtml(currentTemplate, sampleData)
  }, [currentTemplate, sampleData])

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
      name: currentTemplate.name,
      data: {
        subject: editSubject,
        jsxSource: editJsxSource,
      },
    })
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
            {(templates ?? []).map((t) => (
              <TemplateTab
                key={t.name}
                name={t.name}
                active={selectedTemplate === t.name}
                onClick={() => {
                  setSelectedTemplate(t.name)
                  setIsEditing(false)
                  setSaveStatus(null)
                }}
              />
            ))}
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
                    <Button
                      variant="secondary"
                      size="sm"
                      onClick={enterEditMode}
                    >
                      Edit
                    </Button>
                  )}
                </div>
              </div>

              {showSource ? (
                /* JSX Source Code */
                <Card padding="none">
                  <div className="px-6 py-4 border-b border-border-default">
                    <h2 className="text-sm font-semibold text-text-primary">
                      JSX Source — {currentTemplate.name}
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
                      Email Preview — {currentTemplate.name}
                    </h2>
                    <p className="text-xs text-text-tertiary mt-1">
                      Rendered with sample data
                    </p>
                  </div>
                  <div className="p-4 bg-neutral-100">
                    <iframe
                      srcDoc={previewHtml}
                      title={`Preview of ${currentTemplate.name} template`}
                      className="w-full border-0 rounded bg-white"
                      style={{ minHeight: '400px' }}
                      sandbox=""
                    />
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

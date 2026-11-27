import {
  DetailPanel,
  KeyValueTable,
  Mono,
  PayloadView,
  StatusMark,
  Text,
  Timestamp,
} from '@headless/dashboard-sdk/components'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { SandboxedHtmlFrame } from '@headless/dashboard-sdk/preview'
import { DataTable } from '@headless/dashboard-sdk/table'
import { addressLabel, senderLabel, type EmailDetail, type EmailRow } from '../inbox.ts'
import { captureStatus, ChannelView, DeleteAction, DownloadLink, withFailure } from '../parts.tsx'

export default function EmailsView() {
  const format = useFormat()

  return (
    <ChannelView<EmailRow, EmailDetail>
      kind="email"
      title="Emails"
      description="The emails the app tried to send, newest first."
      columns={[
        { id: 'subject', header: 'Subject', cell: (row) => row.subject, isRowHeader: true, width: '3fr' },
        {
          id: 'to',
          header: 'To',
          cell: (row) =>
            row.recipient
              ? row.recipientCount > 1
                ? `${row.recipient} +${format.number(row.recipientCount - 1)}`
                : row.recipient
              : undefined,
          kind: 'id',
          width: '2fr',
        },
        { id: 'sender', header: 'Sender', cell: (row) => senderLabel(row.senderName), priority: 2, width: '1fr' },
        {
          id: 'attachments',
          header: 'Attachments',
          cell: (row) => (row.attachmentCount > 0 ? format.number(row.attachmentCount) : undefined),
          kind: 'number',
          priority: 2,
          width: 110,
        },
        {
          id: 'status',
          header: 'Status',
          cell: (row) => (row.failed ? <StatusMark tone="fail" label="Failed" /> : undefined),
          kind: 'status',
          width: 100,
        },
        {
          id: 'captured',
          header: 'Captured',
          cell: (row) => <Timestamp value={row.capturedAt} />,
          kind: 'time',
          width: 110,
        },
      ]}
      panel={(email) => <EmailPanel email={email} />}
    />
  )
}

function EmailPanel({ email }: { email: EmailDetail }) {
  const path = `dev-inbox/emails/${encodeURIComponent(email.id)}`

  return (
    <DetailPanel
      label={`Email ${email.id}`}
      name={email.subject}
      id={email.id}
      status={captureStatus(email)}
      actions={
        <>
          <DownloadLink path={`${path}/eml`}>Download .eml</DownloadLink>
          <DeleteAction kind="email" id={email.id} name={`The email “${email.subject}”`} />
        </>
      }
      defaultTab={email.hasHtml ? 'html' : email.textBody ? 'text' : 'headers'}
      tabs={withFailure(
        [
          {
            id: 'html',
            label: 'HTML',
            // Mounted only once the email answered with an HTML body: the frame cannot tell a failed load.
            content: email.hasHtml ? <SandboxedHtmlFrame path={`${path}/html`} title={email.subject} /> : null,
          },
          {
            id: 'text',
            label: 'Text',
            content: email.textBody ? <PayloadView content={email.textBody} contentType="text/plain" /> : null,
          },
          { id: 'headers', label: 'Headers', content: <EmailHeaders email={email} /> },
          { id: 'attachments', label: 'Attachments', content: <EmailAttachments email={email} path={path} /> },
        ],
        email.failureError,
      )}
    />
  )
}

function EmailHeaders({ email }: { email: EmailDetail }) {
  const rows = [
    { name: 'From', value: addressLabel(email.from) },
    { name: 'To', value: email.to.map(addressLabel).join(', ') },
    ...(email.cc.length > 0 ? [{ name: 'Cc', value: email.cc.map(addressLabel).join(', ') }] : []),
    ...(email.bcc.length > 0 ? [{ name: 'Bcc', value: email.bcc.map(addressLabel).join(', ') }] : []),
    { name: 'Subject', value: email.subject },
    { name: 'Sender', value: senderLabel(email.senderName) },
    { name: 'Captured at', value: email.capturedAt },
    ...(email.traceId ? [{ name: 'Trace id', value: email.traceId }] : []),
    ...(email.correlationId ? [{ name: 'Correlation id', value: email.correlationId }] : []),
  ]

  return <KeyValueTable label="Headers" rows={rows} />
}

function EmailAttachments({ email, path }: { email: EmailDetail; path: string }) {
  const format = useFormat()

  if (email.attachments.length === 0) {
    return <Text tone="hint">This email has no attachments.</Text>
  }

  return (
    <DataTable
      label="Attachments"
      rows={email.attachments.map((attachment) => ({ ...attachment, id: String(attachment.index) }))}
      emptyState="This email has no attachments."
      columns={[
        { id: 'name', header: 'Name', cell: (row) => <Mono>{row.fileName}</Mono>, isRowHeader: true, width: '2fr' },
        { id: 'type', header: 'Type', cell: (row) => row.contentType, kind: 'id', width: '1fr' },
        { id: 'size', header: 'Size', cell: (row) => format.size(row.sizeBytes), kind: 'number', width: 100 },
        {
          id: 'content',
          header: 'Content',
          cell: (row) =>
            row.isOmitted ? (
              'Content omitted (over the capture limit)'
            ) : (
              <DownloadLink path={`${path}/attachments/${row.index}`}>{`Download ${row.fileName}`}</DownloadLink>
            ),
          width: '2fr',
        },
      ]}
    />
  )
}

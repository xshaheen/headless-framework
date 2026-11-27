import {
  DetailPanel,
  EmptyState,
  FieldGrid,
  GridField,
  KeyValueTable,
  Mono,
  PayloadView,
  StatusMark,
  Tag,
  Timestamp,
} from '@headless/dashboard-sdk/components'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { SandboxedHtmlFrame } from '@headless/dashboard-sdk/preview'
import { DataTable } from '@headless/dashboard-sdk/table'
import { addressLabel, senderLabel, type EmailDetail, type EmailRow } from '../inbox.ts'
import { captureStatus, captureTabs, ChannelView, DeleteAction, DownloadLink } from '../parts.tsx'

export default function EmailsView() {
  const format = useFormat()

  return (
    <ChannelView<EmailRow, EmailDetail>
      kind="email"
      title="Emails"
      description="The emails the app tried to send, newest first."
      columns={[
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
        {
          id: 'subject',
          header: 'Subject',
          // The boards mark an email with attachments on its subject rather than in a column of its own.
          cell: (row) => (
            <>
              {row.subject}
              {row.attachmentCount > 0 ? (
                <>
                  {' '}
                  <Tag>
                    {row.attachmentCount === 1 ? '1 attachment' : `${format.number(row.attachmentCount)} attachments`}
                  </Tag>
                </>
              ) : null}
            </>
          ),
          isRowHeader: true,
          width: '3fr',
        },
        { id: 'sender', header: 'Sender', cell: (row) => senderLabel(row.senderName), priority: 2, width: '1fr' },
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
      tabs={captureTabs(
        [
          {
            id: 'html',
            label: 'HTML',
            // Mounted only once the email answered with an HTML body: the frame cannot tell a failed load.
            content: email.hasHtml ? <SandboxedHtmlFrame path={`${path}/html`} title={email.subject} /> : null,
            facts: <EmailFacts email={email} />,
          },
          {
            id: 'text',
            label: 'Text',
            content: email.textBody ? <PayloadView content={email.textBody} contentType="text/plain" /> : null,
            facts: <EmailFacts email={email} />,
          },
          { id: 'headers', label: 'Headers', content: <EmailHeaders email={email} /> },
          { id: 'attachments', label: 'Attachments', content: <EmailAttachments email={email} path={path} /> },
        ],
        email.failureError,
      )}
    />
  )
}

/** Who sent the email to whom, beside its body, as the board's facts column reads them. */
function EmailFacts({ email }: { email: EmailDetail }) {
  return (
    <FieldGrid label="Message" columns={2}>
      <GridField label="From" span={2}>
        <Mono>{addressLabel(email.from)}</Mono>
      </GridField>
      <GridField label="To" span={2}>
        <Mono>{email.to.map(addressLabel).join(', ')}</Mono>
      </GridField>
      {email.cc.length > 0 ? (
        <GridField label="Cc" span={2}>
          <Mono>{email.cc.map(addressLabel).join(', ')}</Mono>
        </GridField>
      ) : null}
      {email.bcc.length > 0 ? (
        <GridField label="Bcc" span={2}>
          <Mono>{email.bcc.map(addressLabel).join(', ')}</Mono>
        </GridField>
      ) : null}
      <GridField label="Sender">{senderLabel(email.senderName)}</GridField>
      <GridField label="Captured">
        <Timestamp value={email.capturedAt} />
      </GridField>
      {email.traceId ? (
        <GridField label="Trace id" span={2}>
          <Mono>{email.traceId}</Mono>
        </GridField>
      ) : null}
    </FieldGrid>
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
    return <EmptyState title="This email has no attachments." />
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

import {
  DetailPanel,
  FieldGrid,
  GridField,
  KeyValueTable,
  Mono,
  PayloadView,
  StatusMark,
  Timestamp,
} from '@headless/dashboard-sdk/components'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { encodingName, lengthLabel, partsLabel, senderLabel, type SmsDetail, type SmsRow } from '../inbox.ts'
import { captureStatus, captureTabs, ChannelView, DeleteAction } from '../parts.tsx'

export default function SmsView() {
  const format = useFormat()

  return (
    <ChannelView<SmsRow, SmsDetail>
      kind="sms"
      title="SMS"
      description="The text messages the app tried to send, newest first."
      columns={[
        {
          id: 'to',
          header: 'To',
          cell: (row) =>
            row.recipientCount > 1 ? (
              `${format.number(row.recipientCount)} recipients`
            ) : row.recipient ? (
              <Mono>{row.recipient}</Mono>
            ) : undefined,
          isRowHeader: true,
          kind: 'id',
          width: '1.5fr',
        },
        { id: 'text', header: 'Text', cell: (row) => row.snippet, width: '3fr' },
        { id: 'sender', header: 'Sender', cell: (row) => senderLabel(row.senderName), priority: 2, width: '1fr' },
        {
          id: 'parts',
          header: 'Parts',
          cell: (row) => `${encodingName(row.encoding)} · ${partsLabel(row.parts)}`,
          priority: 2,
          width: 140,
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
      panel={(sms) => <SmsPanel sms={sms} />}
    />
  )
}

function SmsPanel({ sms }: { sms: SmsDetail }) {
  const properties = Object.entries(sms.properties ?? {})

  return (
    <DetailPanel
      label={`SMS ${sms.id}`}
      name={sms.recipients.length > 1 ? `${sms.recipients.length} recipients` : (sms.recipients[0] ?? 'SMS')}
      id={sms.id}
      status={captureStatus(sms)}
      actions={<DeleteAction kind="sms" id={sms.id} name={`The SMS to ${sms.recipients[0] ?? 'its recipient'}`} />}
      tabs={captureTabs(
        [
          {
            id: 'message',
            label: 'Message',
            content: <PayloadView content={sms.text} contentType="text/plain" />,
            facts: (
              <>
                <FieldGrid label="How a carrier sends it" columns={3}>
                  <GridField label="Encoding">{encodingName(sms.segmentation.encoding)}</GridField>
                  <GridField label="Length">{lengthLabel(sms.segmentation)}</GridField>
                  <GridField label="Parts">{partsLabel(sms.segmentation.parts)}</GridField>
                </FieldGrid>
                <FieldGrid label="Sent as">
                  <GridField label="Recipients" span={2}>
                    <Mono>{sms.recipients.join(', ')}</Mono>
                  </GridField>
                  <GridField label="Sender">{senderLabel(sms.senderName)}</GridField>
                  <GridField label="Bulk">{sms.isBulk ? 'Yes' : 'No'}</GridField>
                  {sms.messageId ? (
                    <GridField label="Message id" span={2}>
                      <Mono>{sms.messageId}</Mono>
                    </GridField>
                  ) : null}
                  <GridField label="Captured" span={2}>
                    <Timestamp value={sms.capturedAt} />
                  </GridField>
                  {sms.traceId ? (
                    <GridField label="Trace id" span={2}>
                      <Mono>{sms.traceId}</Mono>
                    </GridField>
                  ) : null}
                </FieldGrid>
              </>
            ),
          },
          {
            id: 'properties',
            label: 'Properties',
            content:
              properties.length > 0 ? (
                <KeyValueTable label="Properties" rows={properties.map(([name, value]) => ({ name, value }))} />
              ) : null,
          },
        ],
        sms.failureError,
      )}
    />
  )
}

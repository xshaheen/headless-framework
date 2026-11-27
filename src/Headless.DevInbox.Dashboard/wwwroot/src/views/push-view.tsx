import {
  Banner,
  DetailPanel,
  FieldGrid,
  GridField,
  Mono,
  PayloadView,
  StatusMark,
  Text,
  Timestamp,
} from '@headless/dashboard-sdk/components'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { senderLabel, type PushDetail, type PushRow } from '../inbox.ts'
import { captureStatus, ChannelView, DeleteAction, withFailure } from '../parts.tsx'

export default function PushView() {
  const format = useFormat()

  return (
    <ChannelView<PushRow, PushDetail>
      kind="push"
      title="Push"
      description="The push notifications the app tried to send, newest first."
      columns={[
        {
          id: 'target',
          header: 'Target',
          cell: (row) =>
            row.targetCount > 1 ? (
              `${format.number(row.targetCount)} devices`
            ) : row.target ? (
              <Mono>{row.target}</Mono>
            ) : undefined,
          isRowHeader: true,
          kind: 'id',
          width: '1.5fr',
        },
        {
          id: 'notification',
          header: 'Title and body',
          cell: (row) => (row.isDataOnly ? 'Data-only push' : [row.title, row.body].filter(Boolean).join(' · ')),
          width: '3fr',
        },
        {
          id: 'data',
          header: 'Data',
          cell: (row) => row.dataKeys.join(', ') || undefined,
          kind: 'id',
          priority: 2,
          width: '1.5fr',
        },
        { id: 'sender', header: 'Sender', cell: (row) => senderLabel(row.senderName), priority: 2, width: '1fr' },
        {
          id: 'status',
          header: 'Status',
          cell: (row) =>
            row.failed ? (
              <StatusMark tone="fail" label="Failed" />
            ) : row.exceedsPayloadLimit ? (
              <StatusMark tone="sched" label="Over limit" />
            ) : undefined,
          kind: 'status',
          width: 110,
        },
        {
          id: 'captured',
          header: 'Captured',
          cell: (row) => <Timestamp value={row.capturedAt} />,
          kind: 'time',
          width: 110,
        },
      ]}
      panel={(push) => <PushPanel push={push} />}
    />
  )
}

function PushPanel({ push }: { push: PushDetail }) {
  const format = useFormat()
  const data = push.data && Object.keys(push.data).length > 0 ? JSON.stringify(push.data) : undefined
  const name = push.title || push.body || 'Data-only push'

  return (
    <DetailPanel
      label={`Push ${push.id}`}
      name={name}
      id={push.id}
      status={captureStatus(push)}
      actions={<DeleteAction kind="push" id={push.id} name={`The push “${name}”`} />}
      tabs={withFailure(
        [
          {
            id: 'overview',
            label: 'Overview',
            content: (
              <>
                {push.exceedsPayloadLimit ? (
                  <Banner tone="warn">
                    {`Over the ${format.number(push.maxPayloadBytes)}-byte limit. Firebase Cloud Messaging and APNs would likely reject this push.`}
                  </Banner>
                ) : null}
                {push.isDataOnly ? <Text tone="hint">Data-only push: wakes the app and shows nothing.</Text> : null}
                <FieldGrid label="Notification">
                  {push.title ? (
                    <GridField label="Title" span={2}>
                      {push.title}
                    </GridField>
                  ) : null}
                  {push.body ? (
                    <GridField label="Body" span={2}>
                      {push.body}
                    </GridField>
                  ) : null}
                  <GridField label="Payload size">{`${format.number(push.payloadSizeBytes)} bytes`}</GridField>
                  <GridField label="Sender">{senderLabel(push.senderName)}</GridField>
                  <GridField label="Captured" span={2}>
                    <Timestamp value={push.capturedAt} />
                  </GridField>
                  <GridField label={push.targets.length === 1 ? 'Device token' : 'Device tokens'} span={2}>
                    <Mono>{push.targets.join(', ')}</Mono>
                  </GridField>
                  {push.traceId ? (
                    <GridField label="Trace id" span={2}>
                      <Mono>{push.traceId}</Mono>
                    </GridField>
                  ) : null}
                </FieldGrid>
                <DeliveryOptions push={push} />
              </>
            ),
          },
          {
            id: 'data',
            label: 'Data',
            content: data ? <PayloadView content={data} contentType="application/json" /> : null,
          },
        ],
        push.failureError,
      )}
    />
  )
}

/** Only the delivery options the app set; an unset one would claim a default the provider decides. */
function DeliveryOptions({ push }: { push: PushDetail }) {
  const format = useFormat()
  const options = [
    push.priority ? { label: 'Priority', value: push.priority } : undefined,
    push.timeToLiveSeconds != null
      ? { label: 'Time to live', value: format.duration(push.timeToLiveSeconds * 1000) }
      : undefined,
    push.collapseKey ? { label: 'Collapse key', value: push.collapseKey } : undefined,
    push.badge != null ? { label: 'Badge', value: format.number(push.badge) } : undefined,
    push.sound ? { label: 'Sound', value: push.sound } : undefined,
  ].filter((option) => option !== undefined)

  return options.length > 0 ? (
    <FieldGrid label="Delivery options">
      {options.map((option) => (
        <GridField key={option.label} label={option.label}>
          {option.value}
        </GridField>
      ))}
    </FieldGrid>
  ) : null
}

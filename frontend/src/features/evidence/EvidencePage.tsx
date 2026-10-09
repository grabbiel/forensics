import { ArrowLeft, ArrowsLeftRight, CheckCircle, Hourglass, ShieldCheck, Warning, WarningOctagon } from '@phosphor-icons/react'
import { Fragment, useState, type MouseEvent } from 'react'
import { Link, useFetcher, useLoaderData } from 'react-router'
import type { Anomaly, ChainEvent, EvidenceDetail, TransferView, VerificationReport } from '../../api/evidence'
import { IntegrityBadge } from '../../components/IntegrityBadge'
import { TypeBadge } from '../../components/TypeBadge'
import { ANOMALY_LABELS, describeEvent, formatBytes, formatUtcDateTime, SEVERITY_LABELS } from '../../lib/format'
import type { evidenceLoader, VerifyResult } from './evidenceLoader'

/** One evidence: what it is, who holds it, what is wrong with it, and its custody chain, verifiable on demand. */
export function EvidencePage() {
  const { detail, chain } = useLoaderData<typeof evidenceLoader>()
  const verify = useFetcher<VerifyResult>()
  const verifying = verify.state !== 'idle'
  // The last report survives a later attempt that fails to reach the API; the code check drops another evidence's.
  const [lastReport, setLastReport] = useState<VerificationReport>()
  if (verify.data?.ok && verify.data.report !== lastReport) setLastReport(verify.data.report)
  const report = lastReport?.code === detail.code ? lastReport : undefined
  // A verification just run speaks for the chain until the next load records it.
  const status = report ? (report.valid ? 'Valid' : 'Invalid') : detail.integrity.status

  return (
    <article className="panel" aria-labelledby="evidence-title">
      <div className="panel__head">
        <Link to="/" className="back-link">
          <ArrowLeft size={16} aria-hidden="true" />
          Bandeja
        </Link>
        <h1 id="evidence-title" className="panel__title evidence__code" tabIndex={-1}>
          {detail.code}
        </h1>
        <TypeBadge type={detail.typeCode} />
        <IntegrityBadge status={status} />
      </div>
      <p className="evidence__description">{detail.description}</p>

      <div className="verify">
        {/* aria-disabled, not disabled, so focus stays on the button while it runs. */}
        <button
          type="button"
          className="button button--ghost"
          aria-disabled={verifying}
          onClick={() => verifying || verify.load(`/evidence/${encodeURIComponent(detail.code)}/verify`)}
        >
          <ShieldCheck size={18} aria-hidden="true" />
          Verificar cadena
        </button>
        <VerifyOutcome result={verify.data} verifying={verifying} events={chain.events} />
      </div>

      {detail.anomalies.length > 0 && <Anomalies anomalies={detail.anomalies} />}
      <Facts detail={detail} />
      {detail.pendingTransfer && <PendingTransfer transfer={detail.pendingTransfer} />}
      <Timeline events={chain.events} report={report} />
    </article>
  )
}

/**
 * What the verification found, announced politely. The region is always in the page so the first result is read
 * too; the first invalid event links to its place in the timeline.
 */
function VerifyOutcome({ result, verifying, events }: { result: VerifyResult | undefined; verifying: boolean; events: ChainEvent[] }) {
  return (
    <div className="verify__outcome" role="status">
      {verifying ? (
        'Verificando la cadena…'
      ) : !result ? null : !result.ok ? (
        <span className="verify__error">
          {result.status === 0 ? 'Sin conexión: no se pudo verificar. Inténtalo de nuevo.' : `No se pudo verificar (código ${result.status}).`}
        </span>
      ) : result.report.valid ? (
        <span className="verify__valid">
          <CheckCircle size={18} weight="bold" aria-hidden="true" />
          Cadena íntegra: {result.report.eventCount === 1 ? 'el evento' : `los ${result.report.eventCount} eventos`} y el contenido
          coinciden con lo firmado.
        </span>
      ) : (
        <InvalidOutcome report={result.report} events={events} />
      )}
    </div>
  )
}

function InvalidOutcome({ report, events }: { report: VerificationReport; events: ChainEvent[] }) {
  const invalid = report.firstInvalid!
  const label = `evento #${invalid.seq}`
  // The timeline has a row for this seq when the event is there or, for a gap, its placeholder is; a chain read
  // before newer events were added may have neither.
  const inTimeline = invalid.reason === SEQUENCE_GAP || events.some((event) => event.eventId === invalid.eventId)
  return (
    <span className="verify__invalid">
      <WarningOctagon size={18} weight="bold" aria-hidden="true" />
      <span>
        Cadena alterada en el{' '}
        {inTimeline ? (
          <a href={`#event-${invalid.seq}`} onClick={focusTarget}>
            {label}
          </a>
        ) : (
          label
        )}
        : {invalid.detail} <code className="reason">{invalid.reason}</code>
      </span>
    </span>
  )
}

const SEQUENCE_GAP = 'SEQUENCE_GAP'

/** Moves focus, not only the scroll position, to the timeline row so reading continues from there. */
function focusTarget(event: MouseEvent<HTMLAnchorElement>) {
  const target = document.getElementById(event.currentTarget.hash.slice(1))
  if (!target) return
  event.preventDefault()
  target.focus()
}

/** Overdue-rule findings: icon, severity in words and the rule's own explanation. */
function Anomalies({ anomalies }: { anomalies: Anomaly[] }) {
  return (
    <section className="anomalies" aria-labelledby="anomalies-title">
      <h2 id="anomalies-title" className="section-title">
        Anomalías
      </h2>
      <ul className="anomalies__list">
        {anomalies.map((anomaly) => (
          <li key={`${anomaly.transferId}-${anomaly.kind}`} className={`anomaly anomaly--${anomaly.severity.toLowerCase()}`}>
            <Warning size={20} weight="bold" aria-hidden="true" />
            <div>
              <p className="anomaly__title">
                {ANOMALY_LABELS[anomaly.kind]} <span className="anomaly__severity">{SEVERITY_LABELS[anomaly.severity]}</span>
              </p>
              <p className="anomaly__text">{anomaly.explanation}</p>
            </div>
          </li>
        ))}
      </ul>
    </section>
  )
}

function Facts({ detail }: { detail: EvidenceDetail }) {
  return (
    <dl className="facts">
      <div>
        <dt>Custodio actual</dt>
        <dd>{detail.currentCustodian.displayName}</dd>
      </div>
      <div>
        <dt>Custodio inicial</dt>
        <dd>{detail.initialCustodian.displayName}</dd>
      </div>
      <div>
        <dt>Registrada</dt>
        <dd>
          {formatUtcDateTime(detail.registeredAtUtc)} por {detail.registeredBy.displayName}
        </dd>
      </div>
      <div>
        <dt>Capturada</dt>
        <dd>{formatUtcDateTime(detail.capturedAtUtc)}</dd>
      </div>
      <div>
        <dt>Contenido</dt>
        <dd>
          {detail.content.mediaType} · {formatBytes(detail.content.byteLength)}
        </dd>
      </div>
      <div className="facts__wide">
        <dt>SHA-256 del contenido</dt>
        <dd>
          <code className="hash">{detail.content.sha256}</code>
        </dd>
      </div>
    </dl>
  )
}

/** The transfer waiting on its recipient. */
function PendingTransfer({ transfer }: { transfer: TransferView }) {
  return (
    <section className="pending" aria-labelledby="pending-title">
      <Hourglass size={20} aria-hidden="true" />
      <div>
        <h2 id="pending-title" className="section-title">
          Transferencia pendiente
        </h2>
        <p>
          De {transfer.from.displayName} a <strong>{transfer.to.displayName}</strong>, pedida por {transfer.requestedBy.displayName} el{' '}
          {formatUtcDateTime(transfer.requestedAtUtc)}.
        </p>
        <p className="pending__reason">Motivo: {transfer.reason}</p>
      </div>
    </section>
  )
}

/**
 * The chain in order. After a verification, events up to the last good one are ticked and the first bad one is
 * marked; a missing event gets a row of its own where it should have been.
 */
function Timeline({ events, report }: { events: ChainEvent[]; report: VerificationReport | undefined }) {
  const firstInvalid = report?.firstInvalid ?? undefined
  const gap = firstInvalid?.reason === SEQUENCE_GAP ? firstInvalid : undefined
  const gapRow = gap && (
    <li key="gap" id={`event-${gap.seq}`} className="timeline__item timeline__item--invalid timeline__item--gap" tabIndex={-1}>
      <span className="timeline__seq">#{gap.seq}</span>
      <div className="timeline__body">
        <p className="timeline__what">
          Falta el evento #{gap.seq}
          <strong className="timeline__flag"> · Primer evento inválido</strong>
        </p>
      </div>
    </li>
  )
  // The gap is reported against the event found in its place (none when the chain is empty).
  const gapBefore = gap?.eventId ?? undefined
  return (
    <section className="timeline" aria-labelledby="timeline-title">
      <h2 id="timeline-title" className="section-title">
        Cadena de custodia
      </h2>
      <ol className="timeline__list">
        {events.map((event) => {
          // By event id: the seq a report names may be one the chain lacks.
          const invalid = !gap && event.eventId === firstInvalid?.eventId
          const verified = report !== undefined && event.seq <= report.verifiedThroughSeq
          return (
            <Fragment key={event.eventId}>
              {event.eventId === gapBefore && gapRow}
              <li id={`event-${event.seq}`} className={`timeline__item${invalid ? ' timeline__item--invalid' : ''}`} tabIndex={-1}>
              <span className="timeline__seq">#{event.seq}</span>
              <div className="timeline__body">
                <p className="timeline__what">
                  {event.kind === 'EvidenceRegistered' ? null : <ArrowsLeftRight size={14} aria-hidden="true" />}
                  {describeEvent(event)}
                  {invalid && <strong className="timeline__flag"> · Primer evento inválido</strong>}
                  {verified && (
                    <span className="timeline__ok">
                      <CheckCircle size={14} weight="bold" aria-hidden="true" />
                      <span className="visually-hidden"> (verificado)</span>
                    </span>
                  )}
                </p>
                {event.notes && <p className="timeline__notes">{event.notes}</p>}
                <p className="timeline__mac">
                  MAC <code title={event.mac}>{event.mac.slice(0, 16)}…</code> · clave {event.keyId}
                </p>
              </div>
              <time dateTime={event.occurredAtUtc}>{formatUtcDateTime(event.occurredAtUtc)}</time>
              </li>
            </Fragment>
          )
        })}
        {gap && !events.some((event) => event.eventId === gapBefore) && gapRow}
      </ol>
    </section>
  )
}

import { ArrowLeft } from '@phosphor-icons/react'
import { Link, useLoaderData } from 'react-router'
import { TypeBadge } from '../../components/TypeBadge'
import { describeEvent, formatUtcDateTime } from '../../lib/format'
import type { evidenceLoader } from './evidenceLoader'

/** One evidence: what it is, who holds it and its custody timeline. */
export function EvidencePage() {
  const { detail, chain } = useLoaderData<typeof evidenceLoader>()

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
      </div>
      <p className="evidence__description">{detail.description}</p>

      <dl className="facts">
        <div>
          <dt>Custodio actual</dt>
          <dd>{detail.currentCustodian.displayName}</dd>
        </div>
        <div>
          <dt>Registrada</dt>
          <dd>
            {formatUtcDateTime(detail.registeredAtUtc)} por {detail.registeredBy.displayName}
          </dd>
        </div>
        <div>
          <dt>Eventos</dt>
          <dd>{detail.eventCount}</dd>
        </div>
      </dl>

      <section className="timeline" aria-labelledby="timeline-title">
        <h2 id="timeline-title" className="section-title">
          Cadena de custodia
        </h2>
        <ol className="timeline__list">
          {chain.events.map((event) => (
            <li key={event.eventId} className="timeline__item">
              <span className="timeline__seq">#{event.seq}</span>
              <span>{describeEvent(event)}</span>
              <time dateTime={event.occurredAtUtc}>{formatUtcDateTime(event.occurredAtUtc)}</time>
            </li>
          ))}
        </ol>
      </section>
    </article>
  )
}

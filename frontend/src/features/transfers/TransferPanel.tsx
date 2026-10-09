import { ArrowClockwise, CheckCircle, Hourglass, PaperPlaneTilt, Question, WarningOctagon } from '@phosphor-icons/react'
import { useEffect, useEffectEvent, useRef, useState, type ReactNode, type RefObject } from 'react'
import { useFetcher } from 'react-router'
import type { ProblemDetails } from '../../api/client'
import type { ChainEvent, EvidenceDetail, PersonRef, TransferStatus, TransferView } from '../../api/evidence'
import type { Person } from '../../api/people'
import type { SessionUser } from '../../auth/session'
import { formatUtcDateTime } from '../../lib/format'
import { clearIntent, getIntent, startIntent, type PendingIntent } from './pendingIntent'
import { RejectDialog, RequestDialog, type RequestFields } from './TransferDialogs'
import type { WriteResult } from './transferActions'

const STALE_VERSION = 'urn:evidence-chain:problem:stale-version'

/** What the last write came to, kept until the next one: the page re-reads itself, this stays. */
interface Notice {
  kind: 'success' | 'conflict' | 'unknown' | 'error'
  text: string
  /** The transfer as the server says it is now (409), shown on the card instead of what this page last loaded. */
  state?: { transferId: number; status: TransferStatus; actedBy?: PersonRef; actedAtUtc?: string }
  /** For an unknown outcome: the intent a retry resends, with the same key. */
  retry?: 'request' | 'decision'
}

/**
 * Journey 2 on the evidence page: the pending transfer with the recipient's decision, or the request for a new one.
 * The fetchers live here, keyed by evidence, so they outlive the dialogs and the page's own re-reads.
 */
export function TransferPanel({
  detail,
  chain,
  custodians,
  user,
  headingRef,
}: {
  detail: EvidenceDetail
  chain: ChainEvent[]
  custodians: Person[]
  user: SessionUser
  headingRef: RefObject<HTMLElement | null>
}) {
  const code = detail.code
  const scopes = { request: `request:${code}`, decision: `decision:${code}` }
  const request = useFetcher<WriteResult>({ key: scopes.request })
  const decision = useFetcher<WriteResult>({ key: scopes.decision })
  const [notice, setNotice] = useState<Notice | null>(null)
  const noticeRef = useRef<HTMLDivElement>(null)
  const nameOf = (id: number) => custodians.find((c) => c.id === id)?.displayName ?? `#${id}`

  const pending = detail.pendingTransfer
  const isRecipient = pending !== null && user.role === 'Custodio' && pending.to.id === user.id
  const canRequest = pending === null && user.role === 'Investigador'

  function send(fetcher: typeof request, intent: PendingIntent) {
    const { fields } = intent
    const action = fields.decision ? `/transfers/${fields.transferId}/${fields.decision}` : `/evidence/${encodeURIComponent(code)}/transfer`
    setNotice(null)
    fetcher.submit({ ...fields, idempotencyKey: intent.idempotencyKey }, { method: 'post', action })
  }

  function requestTransfer(fields: RequestFields) {
    send(request, startIntent(scopes.request, { ...fields }))
  }

  function decide(kind: 'accept' | 'reject', transfer: TransferView, text = '') {
    const fields = { decision: kind, transferId: String(transfer.transferId), etag: transfer.etag, evidenceCode: code, [kind === 'accept' ? 'notes' : 'reason']: text }
    send(decision, startIntent(scopes.decision, fields))
  }

  function retry(which: 'request' | 'decision') {
    const intent = getIntent(scopes[which])
    if (intent) send(which === 'request' ? request : decision, intent)
  }

  // Each answer becomes a notice once the page has re-read itself (the fetcher is idle), and moves focus there.
  useSettled(request, (result) => {
    const intent = getIntent(scopes.request)
    if (result.outcome === 'unknown') {
      // The page was just read again: did the request arrive after all?
      const mine = pending && pending.requestedBy.id === user.id && intent && String(pending.to.id) === intent.fields.toCustodianId && pending.reason === intent.fields.reason
      if (mine) {
        clearIntent(scopes.request)
        return setNotice({ kind: 'success', text: `La solicitud sí llegó: queda pendiente de que ${pending.to.displayName} la acepte.` })
      }
      return setNotice({ kind: 'unknown', text: 'No hubo respuesta del servidor y la solicitud no aparece. Reintentar es seguro: con la misma clave, el servidor no la repetirá.', retry: 'request' })
    }
    clearIntent(scopes.request)
    if (result.outcome === 'done') {
      return setNotice({
        kind: 'success',
        text: result.replayed
          ? 'Esta solicitud ya había llegado; el servidor la confirma sin repetirla.'
          : `Solicitud enviada: queda pendiente de que ${result.transfer.to.displayName} la acepte.`,
      })
    }
    setNotice(refusal(result.status, result.problem, 'solicitar la transferencia', nameOf))
  })

  useSettled(decision, (result) => {
    const intent = getIntent(scopes.decision)
    const accepting = intent?.fields.decision !== 'reject'
    if (result.outcome === 'unknown') {
      const decided = intent && chain.some((e) => e.transferId === Number(intent.fields.transferId) && e.actor.id === user.id && e.kind !== 'TransferRequested')
      if (decided) {
        clearIntent(scopes.decision)
        return setNotice({ kind: 'success', text: accepting ? 'La aceptación sí llegó: ahora la custodia es tuya.' : 'El rechazo sí llegó: la custodia sigue donde estaba.' })
      }
      return setNotice({ kind: 'unknown', text: 'No hubo respuesta del servidor y la decisión no aparece. Reintentar es seguro: con la misma clave, el servidor no la repetirá.', retry: 'decision' })
    }
    clearIntent(scopes.decision)
    if (result.outcome === 'done') {
      return setNotice({
        kind: 'success',
        text: accepting ? `Aceptaste la custodia de ${code}.` : `Rechazaste la transferencia; la custodia sigue con ${result.transfer.from.displayName}.`,
      })
    }
    setNotice(refusal(result.status, result.problem, accepting ? 'aceptar' : 'rechazar', nameOf))
  })

  // A new notice takes focus, so it is read and the user is not left on a button that may have gone.
  useEffect(() => {
    if (notice) noticeRef.current?.focus()
  }, [notice])

  const sendingRequest = request.state !== 'idle' ? (request.formData ?? undefined) : undefined
  const deciding = decision.state !== 'idle' ? String(decision.formData?.get('decision') ?? 'accept') : undefined
  const checkingRequest = request.state === 'loading' && request.data?.outcome === 'unknown'
  const checkingDecision = decision.state === 'loading' && decision.data?.outcome === 'unknown'
  const requestIntent = getIntent(scopes.request)

  return (
    <section className="transfers" aria-labelledby="transfers-title">
      <h2 id="transfers-title" className="section-title">
        Transferencia de custodia
      </h2>

      {notice && (
        <div ref={noticeRef} tabIndex={-1} role={notice.kind === 'success' ? 'status' : 'alert'} className={`notice notice--${notice.kind}`}>
          <NoticeIcon kind={notice.kind} />
          <p>{notice.text}</p>
          {notice.retry && (
            <button type="button" className="button button--ghost" onClick={() => retry(notice.retry!)}>
              <ArrowClockwise size={16} aria-hidden="true" />
              Reintentar con la misma clave
            </button>
          )}
        </div>
      )}

      {(checkingRequest || checkingDecision) && (
        <p className="transfer transfer--checking" role="status">
          <Question size={18} aria-hidden="true" />
          Resultado desconocido – comprobando…
        </p>
      )}

      {pending ? (
        <TransferCard
          transfer={pending}
          override={notice?.state?.transferId === pending.transferId ? notice.state : undefined}
          deciding={deciding}
          actions={
            isRecipient && notice?.state?.transferId !== pending.transferId ? (
              <div className="transfer__actions">
                <button
                  type="button"
                  className="button"
                  aria-disabled={deciding !== undefined}
                  onClick={() => deciding === undefined && decide('accept', pending)}
                >
                  Aceptar custodia
                </button>
                <RejectDialog busy={deciding !== undefined} fallbackFocus={headingRef} onSubmit={(reason) => decide('reject', pending, reason)} />
              </div>
            ) : undefined
          }
        />
      ) : sendingRequest && !checkingRequest ? (
        // Ours, not yet the server's: dashed and worded so it never passes for a pending transfer.
        <div className="transfer transfer--sending">
          <PaperPlaneTilt size={20} aria-hidden="true" />
          <div>
            <p className="transfer__status">Enviando…</p>
            <p>
              Solicitud para {nameOf(Number(sendingRequest.get('toCustodianId')))}, aún sin confirmar por el servidor.
            </p>
          </div>
        </div>
      ) : (
        <p className="transfers__none">No hay ninguna transferencia pendiente.</p>
      )}

      {canRequest && (
        <RequestDialog
          custodians={custodians}
          currentCustodianId={detail.currentCustodian.id}
          initial={requestIntent ? { toCustodianId: requestIntent.fields.toCustodianId, reason: requestIntent.fields.reason } : undefined}
          busy={request.state !== 'idle'}
          fallbackFocus={headingRef}
          onSubmit={requestTransfer}
        />
      )}
    </section>
  )
}

/** The pending transfer as the server last said it; after a 409, the state that answer carried. */
function TransferCard({
  transfer,
  override,
  deciding,
  actions,
}: {
  transfer: TransferView
  override?: Notice['state']
  deciding?: string
  actions?: ReactNode
}) {
  const status = override?.status ?? 'Pending'
  return (
    <div className={`transfer transfer--${status.toLowerCase()}`} aria-busy={deciding !== undefined}>
      <Hourglass size={20} aria-hidden="true" />
      <div>
        <p className="transfer__status">
          {status === 'Pending' && 'Pendiente'}
          {status === 'Accepted' && `Aceptada${override?.actedBy ? ` por ${override.actedBy.displayName}` : ''}`}
          {status === 'Rejected' && `Rechazada${override?.actedBy ? ` por ${override.actedBy.displayName}` : ''}`}
          {deciding && ` · enviando ${deciding === 'reject' ? 'rechazo' : 'aceptación'}…`}
        </p>
        <p>
          De {transfer.from.displayName} a <strong>{transfer.to.displayName}</strong>, pedida por {transfer.requestedBy.displayName} el{' '}
          {formatUtcDateTime(transfer.requestedAtUtc)}.
        </p>
        <p className="transfer__reason">Motivo: {transfer.reason}</p>
        {actions}
      </div>
    </div>
  )
}

function NoticeIcon({ kind }: { kind: Notice['kind'] }) {
  if (kind === 'success') return <CheckCircle size={18} weight="bold" aria-hidden="true" />
  if (kind === 'unknown') return <Question size={18} weight="bold" aria-hidden="true" />
  return <WarningOctagon size={18} weight="bold" aria-hidden="true" />
}

/** A refused write in the user's terms. A 409 says who did what and when, from the state the server sent back. */
function refusal(status: number, problem: ProblemDetails | undefined, attempt: string, nameOf: (id: number) => string): Notice {
  const current = problem?.currentState as { transferId: number; status: TransferStatus; toCustodianId: number } | undefined
  if (status === 409 && current) {
    const actedBy = problem?.actedBy as PersonRef | undefined
    const actedAtUtc = problem?.actedAtUtc as string | undefined
    const who = actedBy?.displayName ?? 'Otra persona'
    const when = actedAtUtc ? ` el ${formatUtcDateTime(actedAtUtc)}` : ''
    const state = { transferId: current.transferId, status: current.status, actedBy, actedAtUtc }
    if (problem?.type === STALE_VERSION)
      return { kind: 'conflict', state, text: `No se pudo ${attempt}: la transferencia cambió desde que la abriste. Esto es lo que hay ahora.` }
    if (current.status === 'Accepted') return { kind: 'conflict', state, text: `No se pudo ${attempt}: ${who} ya la aceptó${when}.` }
    if (current.status === 'Rejected') return { kind: 'conflict', state, text: `No se pudo ${attempt}: ${who} ya la rechazó${when}.` }
    return { kind: 'conflict', state, text: `No se pudo ${attempt}: ya hay una transferencia pendiente para ${nameOf(current.toCustodianId)}, pedida por ${who}${when}.` }
  }
  if (status === 409) return { kind: 'conflict', text: `No se pudo ${attempt}: el estado cambió mientras tanto.` }
  if (status === 403) return { kind: 'error', text: `No se pudo ${attempt}: tu rol no lo permite.` }
  if (status === 422) return { kind: 'error', text: `No se pudo ${attempt}: esa petición ya se usó para otra cosa. Vuelve a intentarlo.` }
  const firstError = Object.values(problem?.errors ?? {})[0]?.[0]
  if (status === 400) return { kind: 'error', text: `No se pudo ${attempt}: revisa los datos${firstError ? ` (${firstError})` : ''}.` }
  return { kind: 'error', text: `No se pudo ${attempt} (código ${status}).` }
}

/** Calls `handle` once per answer, when the fetcher is idle again, so the page's re-read has already landed. */
function useSettled(fetcher: { state: string; data?: WriteResult }, handle: (result: WriteResult) => void) {
  const handled = useRef<WriteResult | undefined>(undefined)
  const onSettled = useEffectEvent(handle) // the latest props, without re-running the effect for them
  useEffect(() => {
    if (fetcher.state === 'idle' && fetcher.data && fetcher.data !== handled.current) {
      handled.current = fetcher.data
      onSettled(fetcher.data)
    }
  }, [fetcher.state, fetcher.data])
}

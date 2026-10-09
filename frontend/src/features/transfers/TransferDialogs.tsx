import * as Dialog from '@radix-ui/react-dialog'
import { X } from '@phosphor-icons/react'
import { useId, useRef, useState, type FormEvent, type ReactNode, type RefObject } from 'react'
import type { Person } from '../../api/people'

/**
 * A labelled modal with focus trapped inside; Escape and "Cancelar" close it. Focus goes back to the button that
 * opened it, or to the page heading when that button is gone (it disappears once a transfer is pending).
 */
function TransferDialog({
  open,
  onOpenChange,
  trigger,
  title,
  description,
  fallbackFocus,
  initialFocus,
  children,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  trigger: ReactNode
  title: string
  description: string
  fallbackFocus: RefObject<HTMLElement | null>
  /** The first field, focused on open (Radix would pick the close button). */
  initialFocus: RefObject<HTMLElement | null>
  children: ReactNode
}) {
  const opener = useRef<HTMLButtonElement>(null)
  return (
    <Dialog.Root open={open} onOpenChange={onOpenChange}>
      <Dialog.Trigger asChild ref={opener}>
        {trigger}
      </Dialog.Trigger>
      <Dialog.Portal>
        <Dialog.Overlay className="dialog__overlay" />
        <Dialog.Content
          className="dialog"
          onOpenAutoFocus={(event) => {
            event.preventDefault()
            initialFocus.current?.focus()
          }}
          onCloseAutoFocus={(event) => {
            if (opener.current?.isConnected) return // Radix returns focus to it
            event.preventDefault()
            fallbackFocus.current?.focus()
          }}
        >
          <div className="dialog__head">
            <Dialog.Title className="dialog__title">{title}</Dialog.Title>
            <Dialog.Close className="icon-button" aria-label="Cerrar">
              <X size={18} aria-hidden="true" />
            </Dialog.Close>
          </div>
          <Dialog.Description className="dialog__description">{description}</Dialog.Description>
          {children}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}

export interface RequestFields {
  toCustodianId: string
  reason: string
}

/** A validation message for one field; the field points at it and takes focus. */
interface FieldError {
  field: 'to' | 'reason'
  text: string
}

/** The Investigador's request: who should receive the evidence, and why. */
export function RequestDialog({
  custodians,
  currentCustodianId,
  unresolved,
  throttled = false,
  busy,
  fallbackFocus,
  onSubmit,
}: {
  custodians: Person[]
  currentCustodianId: number
  /** The fields of a request whose outcome is still unknown, so reopening resends the same request. */
  unresolved?: RequestFields
  /** The unresolved request was turned away with a 429: its outcome is known, nothing was saved. */
  throttled?: boolean
  busy: boolean
  fallbackFocus: RefObject<HTMLElement | null>
  onSubmit: (fields: RequestFields) => void
}) {
  const [open, setOpen] = useState(false)
  const [error, setError] = useState<FieldError | null>(null)
  const ids = { to: useId(), reason: useId(), error: useId() }
  const toField = useRef<HTMLSelectElement>(null)
  const reasonField = useRef<HTMLTextAreaElement>(null)
  const recipients = custodians.filter((c) => c.id !== currentCustodianId)
  // Never preselect someone the list no longer offers (they may hold it now): the browser would pick another.
  const initialTo = recipients.some((c) => String(c.id) === unresolved?.toCustodianId) ? unresolved!.toCustodianId : ''

  function fail(field: FieldError['field'], text: string) {
    setError({ field, text })
    ;(field === 'to' ? toField : reasonField).current?.focus()
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const values = { toCustodianId: String(form.get('toCustodianId') ?? ''), reason: String(form.get('reason') ?? '').trim() }
    if (!values.toCustodianId) return fail('to', 'Elige a qué custodio enviarla.')
    if (values.reason.length < 3) return fail('reason', 'Escribe el motivo (al menos 3 caracteres).')
    setError(null)
    setOpen(false)
    onSubmit(values)
  }

  const invalid = (field: FieldError['field']) => (error?.field === field ? { 'aria-invalid': true, 'aria-describedby': ids.error } : {})

  return (
    <TransferDialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next)
        if (!next) setError(null)
      }}
      trigger={
        <button type="button" className="button" aria-disabled={busy} onClick={(event) => busy && event.preventDefault()}>
          Solicitar transferencia
        </button>
      }
      title="Solicitar transferencia"
      description={
        unresolved && throttled
          ? 'Tu última solicitud no se guardó: el servidor recibió demasiadas operaciones seguidas. Puedes enviarla de nuevo con los mismos datos; con otros, será una nueva.'
          : unresolved
            ? 'Tu última solicitud quedó sin confirmar. Enviarla de nuevo con los mismos datos no la duplicará; con otros, será una nueva.'
            : 'La custodia pasará a quien elijas cuando lo acepte. Mientras tanto queda pendiente.'
      }
      fallbackFocus={fallbackFocus}
      initialFocus={toField}
    >
      <form className="dialog__form" onSubmit={submit} noValidate>
        <label htmlFor={ids.to}>Custodio que la recibirá</label>
        <select id={ids.to} ref={toField} name="toCustodianId" className="field" defaultValue={initialTo} required {...invalid('to')}>
          <option value="" disabled>
            Elige un custodio
          </option>
          {recipients.map((c) => (
            <option key={c.id} value={c.id}>
              {c.displayName}
            </option>
          ))}
        </select>
        <label htmlFor={ids.reason}>Motivo</label>
        <textarea
          id={ids.reason}
          ref={reasonField}
          name="reason"
          className="field field--area"
          rows={3}
          maxLength={500}
          defaultValue={unresolved?.reason ?? ''}
          required
          {...invalid('reason')}
        />
        {error && (
          <p id={ids.error} className="field__error" role="alert">
            {error.text}
          </p>
        )}
        <div className="dialog__actions">
          <Dialog.Close className="button button--ghost" type="button">
            Cancelar
          </Dialog.Close>
          <button type="submit" className="button">
            Enviar solicitud
          </button>
        </div>
      </form>
    </TransferDialog>
  )
}

/** The recipient's refusal: a reason is required, the custody stays where it was. */
export function RejectDialog({ busy, fallbackFocus, onSubmit }: { busy: boolean; fallbackFocus: RefObject<HTMLElement | null>; onSubmit: (reason: string) => void }) {
  const [open, setOpen] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const ids = { reason: useId(), error: useId() }
  const firstField = useRef<HTMLTextAreaElement>(null)

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const reason = String(new FormData(event.currentTarget).get('reason') ?? '').trim()
    if (reason.length < 3) {
      setError('Explica por qué la rechazas (al menos 3 caracteres).')
      return firstField.current?.focus()
    }
    setError(null)
    setOpen(false)
    onSubmit(reason)
  }

  return (
    <TransferDialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next)
        if (!next) setError(null)
      }}
      trigger={
        <button type="button" className="button button--ghost" aria-disabled={busy} onClick={(event) => busy && event.preventDefault()}>
          Rechazar
        </button>
      }
      title="Rechazar la transferencia"
      description="La evidencia seguirá con su custodio actual. Quien la pidió verá tu motivo."
      fallbackFocus={fallbackFocus}
      initialFocus={firstField}
    >
      <form className="dialog__form" onSubmit={submit} noValidate>
        <label htmlFor={ids.reason}>Motivo del rechazo</label>
        <textarea id={ids.reason} ref={firstField} name="reason" className="field field--area" rows={3} maxLength={500} required aria-invalid={error ? true : undefined} aria-describedby={error ? ids.error : undefined} />
        {error && (
          <p id={ids.error} className="field__error" role="alert">
            {error}
          </p>
        )}
        <div className="dialog__actions">
          <Dialog.Close className="button button--ghost" type="button">
            Cancelar
          </Dialog.Close>
          <button type="submit" className="button button--danger">
            Rechazar transferencia
          </button>
        </div>
      </form>
    </TransferDialog>
  )
}

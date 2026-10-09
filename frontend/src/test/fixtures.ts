import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

/** People as the API names them in custody data. */
export const person = (id: number, displayName: string) => ({ id, displayName })
export const lucia = person(1, 'Lucía Ferrer')
export const diego = person(4, 'Diego Salas')
export const nuria = person(5, 'Nuria Paredes')

/** What GET /api/v1/people?role=Custodio answers in these tests. */
export const custodians = [
  { ...diego, role: 'Custodio' },
  { ...nuria, role: 'Custodio' },
]

export const UUID_V7 = /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/

/** Fills and sends the request dialog as an Investigador would. */
export async function requestTo(name: string, reason: string) {
  await userEvent.click(screen.getByRole('button', { name: 'Solicitar transferencia' }))
  const dialog = screen.getByRole('dialog', { name: 'Solicitar transferencia' })
  await userEvent.selectOptions(within(dialog).getByLabelText('Custodio que la recibirá'), name)
  await userEvent.clear(within(dialog).getByLabelText('Motivo'))
  await userEvent.type(within(dialog).getByLabelText('Motivo'), reason)
  await userEvent.click(within(dialog).getByRole('button', { name: 'Enviar solicitud' }))
}

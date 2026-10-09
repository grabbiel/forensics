/**
 * A UUIDv7 (RFC 9562): 48 bits of Unix milliseconds, then random bits. Time-ordered, so keys sort by when the user
 * started the write, which also keeps the server's key index tidy.
 */
export function uuidv7(now = Date.now()): string {
  const bytes = crypto.getRandomValues(new Uint8Array(16))
  for (let i = 0; i < 6; i++) bytes[i] = Math.floor(now / 2 ** (8 * (5 - i))) & 0xff
  bytes[6] = (bytes[6] & 0x0f) | 0x70 // version 7
  bytes[8] = (bytes[8] & 0x3f) | 0x80 // RFC variant
  const hex = Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('')
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}

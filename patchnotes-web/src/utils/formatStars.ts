export function formatStars(count: number | undefined): string {
  if (count == null) return ''
  if (count >= 1000) return `${(count / 1000).toFixed(1)}k`
  return String(count)
}

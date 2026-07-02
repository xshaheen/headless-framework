const UNITS = ["B", "KB", "MB", "GB", "TB"] as const;

export function formatBytes(bytes: number): string {
  if (bytes <= 0) {
    return "0 B";
  }

  const exponent = Math.min(Math.floor(Math.log2(bytes) / 10), UNITS.length - 1);
  const value = bytes / 2 ** (10 * exponent);

  return `${value >= 100 || exponent === 0 ? Math.round(value) : value.toFixed(1)} ${UNITS[exponent]}`;
}

export function formatPercent(uploaded: number, total: number): string {
  if (total <= 0) {
    return "0%";
  }

  return `${Math.min(100, Math.floor((uploaded / total) * 100))}%`;
}

export function formatDate(iso: string | null): string {
  if (!iso) {
    return "—";
  }

  const date = new Date(iso);

  return Number.isNaN(date.getTime()) ? "—" : date.toLocaleString();
}

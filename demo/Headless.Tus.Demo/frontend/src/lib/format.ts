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

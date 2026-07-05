export type UploadSummary = {
  id: string;
  fileName: string;
  contentType: string;
  committedBytes: number;
  totalBytes: number | null;
  isComplete: boolean;
  createdAt: string | null;
  expiresAt: string | null;
};

export async function listUploads(): Promise<UploadSummary[]> {
  const response = await fetch("/api/files");

  if (!response.ok) {
    throw new Error(`Listing failed: HTTP ${response.status}`);
  }

  return (await response.json()) as UploadSummary[];
}

/** tus Termination extension: DELETE on the upload URL removes the upload and its data. */
export async function terminateUpload(id: string): Promise<void> {
  const response = await fetch(`/files/${encodeURIComponent(id)}`, {
    method: "DELETE",
    headers: { "Tus-Resumable": "1.0.0" },
  });

  if (!response.ok && response.status !== 404) {
    throw new Error(`Delete failed: HTTP ${response.status}`);
  }
}

export function downloadUrl(id: string): string {
  return `/api/files/${encodeURIComponent(id)}/download`;
}

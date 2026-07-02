import { downloadUrl, terminateUpload } from "../lib/api";
import { formatBytes, formatDate } from "../lib/format";
import type { UploadSummary } from "../lib/api";

export type UploadedFilesProps = {
  files: UploadSummary[];
  isLoading: boolean;
  loadError: string | null;
  onRefresh: () => void;
};

export function UploadedFiles({ files, isLoading, loadError, onRefresh }: UploadedFilesProps) {
  const handleDelete = async (id: string) => {
    await terminateUpload(id);
    onRefresh();
  };

  return (
    <section className="panel">
      <div className="panel__header">
        <h2>Stored uploads</h2>
        <button type="button" onClick={onRefresh} disabled={isLoading}>
          {isLoading ? "Refreshing…" : "Refresh"}
        </button>
      </div>

      {loadError && <p className="panel__error">{loadError}</p>}

      {files.length === 0 && !loadError ? (
        <p className="panel__empty">Nothing stored yet — upload something above.</p>
      ) : (
        <table className="files">
          <thead>
            <tr>
              <th>Name</th>
              <th>Size</th>
              <th>Status</th>
              <th>Expires</th>
              <th aria-label="Actions" />
            </tr>
          </thead>
          <tbody>
            {files.map((file) => (
              <tr key={file.id}>
                <td className="files__name" title={file.id}>
                  {file.fileName}
                </td>
                <td>
                  {file.isComplete
                    ? formatBytes(file.committedBytes)
                    : `${formatBytes(file.committedBytes)} of ${file.totalBytes === null ? "?" : formatBytes(file.totalBytes)}`}
                </td>
                <td>
                  <span className={`badge badge--${file.isComplete ? "complete" : "partial"}`}>
                    {file.isComplete ? "Complete" : "Incomplete"}
                  </span>
                </td>
                <td>{file.isComplete ? "Never (kept)" : formatDate(file.expiresAt)}</td>
                <td className="files__actions">
                  {file.isComplete && (
                    <a href={downloadUrl(file.id)} download={file.fileName}>
                      Download
                    </a>
                  )}
                  <button type="button" className="danger" onClick={() => void handleDelete(file.id)}>
                    Delete
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}

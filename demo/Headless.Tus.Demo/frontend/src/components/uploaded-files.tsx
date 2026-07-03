import prettyBytes from "pretty-bytes";
import { downloadUrl } from "../lib/api";
import { formatDate } from "../lib/format";
import { useDeleteUpload, useUploadsQuery } from "../lib/queries";

export function UploadedFiles() {
  const { data: files = [], isFetching, error, refetch } = useUploadsQuery();
  const deleteUpload = useDeleteUpload();

  return (
    <section className="panel">
      <div className="panel__header">
        <h2>Stored uploads</h2>
        <button type="button" onClick={() => void refetch()} disabled={isFetching}>
          {isFetching ? "Refreshing…" : "Refresh"}
        </button>
      </div>

      {error && <p className="panel__error">{error.message}</p>}

      {files.length === 0 && !error ? (
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
                    ? prettyBytes(file.committedBytes)
                    : `${prettyBytes(file.committedBytes)} of ${file.totalBytes === null ? "?" : prettyBytes(file.totalBytes)}`}
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
                  <button
                    type="button"
                    className="danger"
                    disabled={deleteUpload.isPending}
                    onClick={() => deleteUpload.mutate(file.id)}
                  >
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

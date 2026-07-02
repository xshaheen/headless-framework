import { formatBytes, formatPercent } from "../lib/format";
import type { ActiveUpload } from "../hooks/use-tus-uploads";

export type ActiveUploadCardProps = {
  upload: ActiveUpload;
  onPause: (id: string) => void;
  onResume: (id: string) => void;
  onCancel: (id: string) => void;
  onDismiss: (id: string) => void;
};

const STATE_LABELS: Record<ActiveUpload["state"], string> = {
  uploading: "Uploading",
  paused: "Paused",
  error: "Failed",
  done: "Completed",
};

export function ActiveUploadCard({ upload, onPause, onResume, onCancel, onDismiss }: ActiveUploadCardProps) {
  const percent = formatPercent(upload.uploadedBytes, upload.totalBytes);

  return (
    <li className={`upload-card upload-card--${upload.state}`}>
      <div className="upload-card__header">
        <span className="upload-card__name" title={upload.fileName}>
          {upload.fileName}
        </span>
        <span className="upload-card__state">{STATE_LABELS[upload.state]}</span>
      </div>

      <div className="progress">
        <div className="progress__bar" style={{ width: percent }} />
      </div>

      <div className="upload-card__footer">
        <span className="upload-card__bytes">
          {formatBytes(upload.uploadedBytes)} / {formatBytes(upload.totalBytes)} ({percent})
        </span>

        <span className="upload-card__actions">
          {upload.state === "uploading" && (
            <button type="button" onClick={() => onPause(upload.id)}>
              Pause
            </button>
          )}
          {(upload.state === "paused" || upload.state === "error") && (
            <button type="button" onClick={() => onResume(upload.id)}>
              Resume
            </button>
          )}
          {upload.state === "done" ? (
            <button type="button" onClick={() => onDismiss(upload.id)}>
              Dismiss
            </button>
          ) : (
            <button type="button" className="danger" onClick={() => onCancel(upload.id)}>
              Cancel
            </button>
          )}
        </span>
      </div>

      {upload.error && upload.state === "error" && <p className="upload-card__error">{upload.error}</p>}
    </li>
  );
}

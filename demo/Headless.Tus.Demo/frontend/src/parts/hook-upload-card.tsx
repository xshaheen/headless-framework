import prettyBytes from "pretty-bytes";
import { useEffect, useRef, useState } from "react";
import { useTus } from "use-tus";
import { formatPercent } from "../lib/format";
import { useInvalidateUploads } from "../lib/queries";

export type HookUploadCardProps = {
  id: string;
  file: File;
  onRemove: (id: string) => void;
};

/**
 * One transfer owned by one `useTus` instance (the hook manages a single tus.Upload). With
 * `autoStart` the hook also resumes fingerprint-matched previous uploads before starting.
 */
export function HookUploadCard({ id, file, onRemove }: HookUploadCardProps) {
  const invalidateUploads = useInvalidateUploads();
  const { upload, setUpload, isSuccess, isAborted, isUploading, error } = useTus({ autoStart: true });
  const [uploadedBytes, setUploadedBytes] = useState(0);
  const started = useRef(false);

  useEffect(() => {
    // Guard StrictMode's double effect invocation: a second setUpload would create (and start) a
    // second tus.Upload against the same fingerprint.
    if (started.current) {
      return;
    }

    started.current = true;

    setUpload(file, {
      endpoint: "/files",
      chunkSize: 5 * 1024 * 1024,
      retryDelays: [0, 1000, 3000, 5000],
      removeFingerprintOnSuccess: true,
      metadata: {
        filename: file.name,
        filetype: file.type || "application/octet-stream",
      },
      onProgress: (bytesSent) => setUploadedBytes(bytesSent),
      onSuccess: () => invalidateUploads(),
    });
  }, [file, invalidateUploads, setUpload]);

  // isUploading is checked before error so a resumed/retrying upload shows "uploading" (and the Pause
  // button) instead of the stale "Failed" state — use-tus's onStart resets isUploading but never clears
  // error (only remove() does, which this card never calls).
  const state = isSuccess ? "done" : isUploading ? "uploading" : error ? "error" : isAborted ? "paused" : "starting";
  const stateLabel = { done: "Completed", error: "Failed", paused: "Paused", uploading: "Uploading", starting: "Starting…" }[
    state
  ];
  const percent = formatPercent(isSuccess ? file.size : uploadedBytes, file.size);

  const handleCancel = () => {
    // tus DELETE — the server discards the partial upload. abort(true) rejects with a DetailedError for
    // any non-204 terminate response (e.g. an already-deleted 404) once retries are exhausted; catch it so
    // a failed/duplicate cancel does not surface as an unhandled promise rejection.
    upload?.abort(true).catch((err) => console.error("Failed to terminate upload", err));
    onRemove(id);
  };

  return (
    <li className={`upload-card upload-card--${state}`}>
      <div className="upload-card__header">
        <span className="upload-card__name" title={file.name}>
          {file.name}
        </span>
        <span className="upload-card__state">{stateLabel}</span>
      </div>

      <div className="progress">
        <div className="progress__bar" style={{ width: percent }} />
      </div>

      <div className="upload-card__footer">
        <span className="upload-card__bytes">
          {prettyBytes(isSuccess ? file.size : uploadedBytes)} / {prettyBytes(file.size)} ({percent})
        </span>

        <span className="upload-card__actions">
          {state === "uploading" && (
            <button type="button" onClick={() => void upload?.abort()}>
              Pause
            </button>
          )}
          {(state === "paused" || state === "error") && (
            <button type="button" onClick={() => upload?.start()}>
              Resume
            </button>
          )}
          {state === "done" ? (
            <button type="button" onClick={() => onRemove(id)}>
              Dismiss
            </button>
          ) : (
            <button type="button" className="danger" onClick={handleCancel}>
              Cancel
            </button>
          )}
        </span>
      </div>

      {error && state === "error" && <p className="upload-card__error">{error.message}</p>}
    </li>
  );
}

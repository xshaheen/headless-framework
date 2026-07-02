import { useCallback, useRef, useState } from "react";
import { Upload } from "tus-js-client";

export type UploadState = "uploading" | "paused" | "error" | "done";

export type ActiveUpload = {
  id: string;
  fileName: string;
  totalBytes: number;
  uploadedBytes: number;
  state: UploadState;
  error?: string;
};

export type UseTusUploadsResult = {
  uploads: ActiveUpload[];
  addFiles: (files: FileList | File[]) => void;
  pause: (id: string) => void;
  resume: (id: string) => void;
  cancel: (id: string) => void;
  dismiss: (id: string) => void;
};

/**
 * Manages tus uploads against the demo backend. tus-js-client owns the transfer (5 MB PATCH
 * chunks, retry, fingerprint-based resume across page reloads); this hook mirrors the transfer
 * state into React state. `pause` aborts the requests without discarding server-side progress —
 * `resume` HEADs the upload URL and continues from the committed offset.
 */
export function useTusUploads(onCompleted: () => void): UseTusUploadsResult {
  const [uploads, setUploads] = useState<ActiveUpload[]>([]);
  const instances = useRef(new Map<string, Upload>());
  // Uploads the user paused; checked by the async resume continuation so a Pause/Cancel clicked
  // while findPreviousUploads() is still pending cannot start a transfer the UI no longer tracks.
  const pausedIds = useRef(new Set<string>());

  const patch = useCallback((id: string, changes: Partial<ActiveUpload>) => {
    setUploads((current) => current.map((u) => (u.id === id ? { ...u, ...changes } : u)));
  }, []);

  const addFiles = useCallback(
    (files: FileList | File[]) => {
      for (const file of Array.from(files)) {
        const id = crypto.randomUUID();

        const upload = new Upload(file, {
          endpoint: "/files",
          chunkSize: 5 * 1024 * 1024,
          retryDelays: [0, 1000, 3000, 5000],
          removeFingerprintOnSuccess: true,
          metadata: {
            filename: file.name,
            filetype: file.type || "application/octet-stream",
          },
          onProgress: (uploaded) => patch(id, { uploadedBytes: uploaded }),
          onSuccess: () => {
            patch(id, { state: "done", uploadedBytes: file.size });
            onCompleted();
          },
          onError: (error) => patch(id, { state: "error", error: error.message }),
        });

        instances.current.set(id, upload);
        setUploads((current) => [
          ...current,
          { id, fileName: file.name, totalBytes: file.size, uploadedBytes: 0, state: "uploading" },
        ]);

        // Resume an interrupted upload of the same file (fingerprint match) instead of restarting.
        void upload.findPreviousUploads().then((previous) => {
          // Cancelled/dismissed (instance replaced or removed) or paused while the lookup ran.
          if (instances.current.get(id) !== upload || pausedIds.current.has(id)) {
            return;
          }

          if (previous.length > 0) {
            upload.resumeFromPreviousUpload(previous[0]);
          }

          upload.start();
        });
      }
    },
    [onCompleted, patch],
  );

  const pause = useCallback(
    (id: string) => {
      pausedIds.current.add(id);
      void instances.current.get(id)?.abort();
      patch(id, { state: "paused" });
    },
    [patch],
  );

  const resume = useCallback(
    (id: string) => {
      const upload = instances.current.get(id);

      if (!upload) {
        return;
      }

      pausedIds.current.delete(id);
      patch(id, { state: "uploading", error: undefined });
      upload.start();
    },
    [patch],
  );

  const cancel = useCallback((id: string) => {
    const upload = instances.current.get(id);

    // abort(true) sends the tus DELETE (Termination) so the server discards the partial upload.
    void upload?.abort(true);
    instances.current.delete(id);
    pausedIds.current.delete(id);
    setUploads((current) => current.filter((u) => u.id !== id));
  }, []);

  const dismiss = useCallback((id: string) => {
    instances.current.delete(id);
    pausedIds.current.delete(id);
    setUploads((current) => current.filter((u) => u.id !== id));
  }, []);

  return { uploads, addFiles, pause, resume, cancel, dismiss };
}

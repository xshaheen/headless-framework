import { useCallback, useEffect, useState } from "react";
import { ActiveUploadCard } from "./components/active-upload-card";
import { UploadDropzone } from "./components/upload-dropzone";
import { UploadedFiles } from "./components/uploaded-files";
import { useTusUploads } from "./hooks/use-tus-uploads";
import { listUploads } from "./lib/api";
import type { UploadSummary } from "./lib/api";

export function App() {
  const [files, setFiles] = useState<UploadSummary[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);

  const refresh = useCallback(() => {
    setIsLoading(true);

    listUploads()
      .then((result) => {
        setFiles(result);
        setLoadError(null);
      })
      .catch((error: unknown) => {
        setLoadError(error instanceof Error ? error.message : "Failed to load uploads");
      })
      .finally(() => setIsLoading(false));
  }, []);

  useEffect(() => {
    refresh();
  }, [refresh]);

  const { uploads, addFiles, pause, resume, cancel, dismiss } = useTusUploads(refresh);

  return (
    <main className="layout">
      <header className="hero">
        <h1>Resumable uploads with tus</h1>
        <p>
          <code>tusdotnet</code> + <code>Headless.Tus.Azure</code> (Azure Block Blob store) behind the tus 1.0.0
          protocol — creation, resumable PATCH, expiration, and termination, driven by{" "}
          <code>tus-js-client</code>.
        </p>
      </header>

      <UploadDropzone onFiles={addFiles} />

      {uploads.length > 0 && (
        <ul className="upload-list">
          {uploads.map((upload) => (
            <ActiveUploadCard
              key={upload.id}
              upload={upload}
              onPause={pause}
              onResume={resume}
              onCancel={cancel}
              onDismiss={dismiss}
            />
          ))}
        </ul>
      )}

      <UploadedFiles files={files} isLoading={isLoading} loadError={loadError} onRefresh={refresh} />
    </main>
  );
}

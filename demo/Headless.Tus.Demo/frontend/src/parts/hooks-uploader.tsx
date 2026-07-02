import { useCallback, useState } from "react";
import { useDropzone } from "react-dropzone";
import { HookUploadCard } from "./hook-upload-card";

type PendingFile = {
  key: string;
  file: File;
};

export function HooksUploader() {
  const [files, setFiles] = useState<PendingFile[]>([]);

  const onDrop = useCallback((accepted: File[]) => {
    setFiles((current) => [...current, ...accepted.map((file) => ({ key: crypto.randomUUID(), file }))]);
  }, []);

  const removeFile = useCallback((key: string) => {
    setFiles((current) => current.filter((pending) => pending.key !== key));
  }, []);

  const { getRootProps, getInputProps, isDragActive } = useDropzone({ onDrop });

  return (
    <>
      <div {...getRootProps({ className: `dropzone${isDragActive ? " dropzone--active" : ""}` })}>
        <input {...getInputProps()} />
        <span className="dropzone__title">Drop files here or click to choose</span>
        <span className="dropzone__hint">
          react-dropzone picks the files, one <code>use-tus</code> hook per card drives the transfer, and
          TanStack Query keeps the stored list fresh. Pause mid-flight — or re-add the same file after a
          reload — and the upload resumes from the last committed byte.
        </span>
      </div>

      {files.length > 0 && (
        <ul className="upload-list">
          {files.map(({ key, file }) => (
            <HookUploadCard key={key} id={key} file={file} onRemove={removeFile} />
          ))}
        </ul>
      )}
    </>
  );
}

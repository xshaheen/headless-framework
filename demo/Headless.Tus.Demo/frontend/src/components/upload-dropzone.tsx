import { useRef, useState } from "react";

export type UploadDropzoneProps = {
  onFiles: (files: FileList | File[]) => void;
};

export function UploadDropzone({ onFiles }: UploadDropzoneProps) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [isDragOver, setIsDragOver] = useState(false);

  const handleDrop = (e: React.DragEvent<HTMLButtonElement>) => {
    e.preventDefault();
    setIsDragOver(false);

    if (e.dataTransfer.files.length > 0) {
      onFiles(e.dataTransfer.files);
    }
  };

  const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files.length > 0) {
      onFiles(e.target.files);
      e.target.value = "";
    }
  };

  return (
    <button
      type="button"
      className={`dropzone${isDragOver ? " dropzone--active" : ""}`}
      onClick={() => inputRef.current?.click()}
      onDragOver={(e) => {
        e.preventDefault();
        setIsDragOver(true);
      }}
      onDragLeave={() => setIsDragOver(false)}
      onDrop={handleDrop}
    >
      <span className="dropzone__title">Drop files here or click to choose</span>
      <span className="dropzone__hint">
        Uploads are resumable — pause them, kill the tab, or drop the connection and they continue
        from the last committed byte. Non-ASCII filenames welcome.
      </span>
      <input ref={inputRef} type="file" multiple hidden onChange={handleChange} />
    </button>
  );
}

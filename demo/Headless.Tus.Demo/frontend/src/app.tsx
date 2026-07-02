import { Suspense, lazy, useState } from "react";
import { UploadedFiles } from "./components/uploaded-files";
import { HooksUploader } from "./parts/hooks-uploader";

// Uppy (dashboard UI + plugins) is by far the heaviest dependency; load it only when its tab shows.
const UppyUploader = lazy(() =>
  import("./parts/uppy-uploader").then((module) => ({ default: module.UppyUploader })),
);

type Part = "uppy" | "hooks";

const PARTS: Record<Part, { label: string; blurb: string }> = {
  uppy: {
    label: "Uppy Dashboard",
    blurb: "The batteries-included path: @uppy/dashboard + @uppy/tus render a full uploader UI over the same endpoint.",
  },
  hooks: {
    label: "React hooks",
    blurb: "The hand-rolled path: react-dropzone + use-tus + TanStack Query + pretty-bytes, protocol mechanics visible.",
  },
};

export function App() {
  const [part, setPart] = useState<Part>("uppy");

  return (
    <main className="layout">
      <header className="hero">
        <h1>Resumable uploads with tus</h1>
        <p>
          <code>tusdotnet</code> + <code>Headless.Tus.Azure</code> (Azure Block Blob store) behind the tus 1.0.0
          protocol — the same <code>/files</code> endpoint driven by two different client stacks.
        </p>
      </header>

      <nav className="tabs" aria-label="Uploader implementation">
        {(Object.keys(PARTS) as Part[]).map((key) => (
          <button
            key={key}
            type="button"
            className={`tab${part === key ? " tab--active" : ""}`}
            aria-pressed={part === key}
            onClick={() => setPart(key)}
          >
            {PARTS[key].label}
          </button>
        ))}
      </nav>

      <p className="tabs__blurb">{PARTS[part].blurb}</p>

      {/* Both parts stay mounted; the inactive one is only hidden. Unmounting would abort
          in-flight use-tus transfers and detach the Uppy complete listener while its singleton
          keeps uploading in the background. */}
      <div hidden={part !== "uppy"}>
        <Suspense fallback={<p className="tabs__blurb">Loading Uppy…</p>}>
          <UppyUploader />
        </Suspense>
      </div>
      <div hidden={part !== "hooks"}>
        <HooksUploader />
      </div>

      <UploadedFiles />
    </main>
  );
}

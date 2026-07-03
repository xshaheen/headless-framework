import Uppy from "@uppy/core";
import Dashboard from "@uppy/react/dashboard";
import Tus from "@uppy/tus";
import { useEffect } from "react";
import { useInvalidateUploads } from "../lib/queries";

import "@uppy/core/css/style.min.css";
import "@uppy/dashboard/css/style.min.css";

// Module-level singleton so switching tabs never interrupts in-flight uploads. @uppy/tus maps
// each file's name/type meta onto the tus Upload-Metadata filename/filetype fields automatically.
const uppy = new Uppy({
  restrictions: { maxFileSize: 2 * 1024 * 1024 * 1024 },
}).use(Tus, {
  endpoint: "/files",
  chunkSize: 5 * 1024 * 1024,
  retryDelays: [0, 1000, 3000, 5000],
  removeFingerprintOnSuccess: true,
});

export function UppyUploader() {
  const invalidateUploads = useInvalidateUploads();

  useEffect(() => {
    uppy.on("complete", invalidateUploads);

    return () => {
      uppy.off("complete", invalidateUploads);
    };
  }, [invalidateUploads]);

  return (
    <Dashboard
      uppy={uppy}
      theme="dark"
      width="100%"
      height={330}
      proudlyDisplayPoweredByUppy={false}
      note="Uploads go through @uppy/tus — pause/resume from the file's menu."
    />
  );
}

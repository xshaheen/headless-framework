import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

// Dev mode proxies the tus endpoint and the demo API to the .NET backend so the browser sees a
// single origin (tus Location headers then stay on the dev origin). The production build emits
// into the demo's wwwroot, which the backend serves directly.
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: "../wwwroot",
    emptyOutDir: true,
  },
  server: {
    proxy: {
      "/files": "http://localhost:5273",
      "/api": "http://localhost:5273",
    },
  },
});

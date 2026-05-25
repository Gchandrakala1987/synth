import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// During dev, the React app calls the .NET API directly. CORS is configured
// server-side; no proxy needed. In production both are served from the same origin
// (App Service) so VITE_API_BASE_URL ends up empty.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true
  },
  build: {
    outDir: "dist",
    sourcemap: true
  }
});

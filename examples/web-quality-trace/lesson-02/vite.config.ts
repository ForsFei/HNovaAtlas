import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [react()],
  build: { outDir: ".local/dist" },
  server: {
    host: "127.0.0.1",
    port: 43112,
    strictPort: true,
    proxy: {
      "/api/live": { target: "http://127.0.0.1:43111", ws: true },
      "/api": "http://127.0.0.1:43111",
    },
  },
});

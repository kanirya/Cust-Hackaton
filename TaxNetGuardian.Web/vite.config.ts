import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: "../TaxNetGuardian.Api/wwwroot",
    emptyOutDir: true
  },
  server: {
    proxy: {
      "/api": "http://localhost:5187",
      "/sandbox": "http://localhost:5187"
    }
  }
});

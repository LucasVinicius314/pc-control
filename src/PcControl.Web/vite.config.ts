import { defineConfig } from "vite";

// Relative base: the page is served from the PC app's own root, embedded in its executable.
export default defineConfig({
  base: "./",
  build: {
    outDir: "dist",
    emptyOutDir: true,
    target: "es2022",
    // Two pages: the phone remote, and the controller UI shown on the PC itself.
    rollupOptions: { input: { main: "index.html", couch: "couch.html" } },
    assetsInlineLimit: 0,
  },
});

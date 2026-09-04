import { defineConfig } from "orval";

export default defineConfig({
  patchnotes: {
    output: {
      mode: "tags-split",
      target: "src/api/generated",
      schemas: "src/api/generated/model",
      client: "react-query",
      override: {
        mutator: {
          path: "./src/api/custom-fetch.ts",
          name: "customFetch",
        },
      },
    },
    input: {
      target: "./openapi.json",
    },
  },
  patchnotesZod: {
    output: {
      mode: "tags-split",
      target: "src/api/generated",
      client: "zod",
      fileExtension: ".zod.ts",
      override: {
        zod: {
          dateTimeOptions: {
            offset: true,
          },
        },
      },
    },
    input: {
      target: "./openapi.json",
    },
  },
});

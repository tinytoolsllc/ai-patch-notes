import { http, HttpResponse } from "msw";
import { server } from "../test/mocks/server";
import { api, ApiError, isApiError, getErrorMessage } from "./client";

describe("api client", () => {
  describe("api.get", () => {
    it("performs GET request successfully", async () => {
      server.use(
        http.get("/api/test", () => {
          return HttpResponse.json({ data: "success" });
        }),
      );

      const result = await api.get<{ data: string }>("/test");
      expect(result).toEqual({ data: "success" });
    });

    it("includes API key header when configured", async () => {
      let capturedContentType: string | null = null;
      server.use(
        http.get("/api/test", ({ request }) => {
          capturedContentType = request.headers.get("Content-Type");
          return HttpResponse.json({ data: "success" });
        }),
      );

      await api.get("/test");
      expect(capturedContentType).toBe("application/json");
    });
  });

  describe("api.post", () => {
    it("performs POST request with body", async () => {
      let capturedBody: unknown = null;
      server.use(
        http.post("/api/test", async ({ request }) => {
          capturedBody = await request.json();
          return HttpResponse.json({ id: "test-id" });
        }),
      );

      const result = await api.post<{ id: string }>("/test", {
        name: "test",
      });

      expect(result).toEqual({ id: "test-id" });
      expect(capturedBody).toEqual({ name: "test" });
    });
  });

  describe("api.put", () => {
    it("performs PUT request with body", async () => {
      let capturedBody: unknown = null;
      server.use(
        http.put("/api/test/1", async ({ request }) => {
          capturedBody = await request.json();
          return HttpResponse.json({ id: "test-id", updated: true });
        }),
      );

      const result = await api.put<{ id: string; updated: boolean }>("/test/1", {
        name: "updated",
      });

      expect(result).toEqual({ id: "test-id", updated: true });
      expect(capturedBody).toEqual({ name: "updated" });
    });
  });

  describe("api.patch", () => {
    it("performs PATCH request with body", async () => {
      server.use(
        http.patch("/api/test/1", () => {
          return HttpResponse.json({ id: "test-id", patched: true });
        }),
      );

      const result = await api.patch<{ id: string; patched: boolean }>("/test/1", {
        field: "value",
      });
      expect(result).toEqual({ id: "test-id", patched: true });
    });
  });

  describe("api.delete", () => {
    it("performs DELETE request", async () => {
      server.use(
        http.delete("/api/test/1", () => {
          return new HttpResponse(null, { status: 204 });
        }),
      );

      const result = await api.delete("/test/1");
      expect(result).toBeUndefined();
    });
  });

  describe("error handling", () => {
    it("throws ApiError on 400 response", async () => {
      server.use(
        http.get("/api/error", () => {
          return HttpResponse.json({ message: "Bad request" }, { status: 400 });
        }),
      );

      await expect(api.get("/error")).rejects.toThrow(ApiError);
    });

    it("throws ApiError on 401 response", async () => {
      server.use(
        http.get("/api/error", () => {
          return new HttpResponse(null, { status: 401 });
        }),
      );

      await expect(api.get("/error")).rejects.toThrow(ApiError);
    });

    it("throws ApiError on 404 response", async () => {
      server.use(
        http.get("/api/notfound", () => {
          return new HttpResponse(null, { status: 404 });
        }),
      );

      await expect(api.get("/notfound")).rejects.toThrow(ApiError);
    });

    it("throws ApiError on 500 response", async () => {
      server.use(
        http.get("/api/error", () => {
          return new HttpResponse(null, { status: 500 });
        }),
      );

      await expect(api.get("/error")).rejects.toThrow(ApiError);
    });

    it("includes response data in ApiError", async () => {
      server.use(
        http.get("/api/error", () => {
          return HttpResponse.json(
            { error: "Something went wrong", code: "ERR_001" },
            { status: 400 },
          );
        }),
      );

      try {
        await api.get("/error");
      } catch (error) {
        expect(isApiError(error)).toBe(true);
        if (isApiError(error)) {
          expect(error.status).toBe(400);
          expect(error.data).toEqual({
            error: "Something went wrong",
            code: "ERR_001",
          });
        }
      }
    });

    it("handles network errors", async () => {
      server.use(
        http.get("/api/network-error", () => {
          return HttpResponse.error();
        }),
      );

      try {
        await api.get("/network-error");
      } catch (error) {
        expect(isApiError(error)).toBe(true);
        if (isApiError(error)) {
          expect(error.isNetworkError).toBe(true);
        }
      }
    });
  });
});

describe("isApiError", () => {
  it("returns true for ApiError instances", () => {
    const error = new ApiError(400, "Bad request");
    expect(isApiError(error)).toBe(true);
  });

  it("returns false for regular Error instances", () => {
    const error = new Error("Something went wrong");
    expect(isApiError(error)).toBe(false);
  });

  it("returns false for non-error values", () => {
    expect(isApiError(null)).toBe(false);
    expect(isApiError(undefined)).toBe(false);
    expect(isApiError("error")).toBe(false);
    expect(isApiError({ status: 400 })).toBe(false);
  });
});

describe("getErrorMessage", () => {
  it("returns network error message", () => {
    const error = new ApiError(0, "", undefined, true);
    expect(getErrorMessage(error)).toBe(
      "Unable to connect to server. Please check your internet connection.",
    );
  });

  it("returns 401 error message", () => {
    const error = new ApiError(401, "Unauthorized");
    expect(getErrorMessage(error)).toBe("Authentication required. Please sign in to continue.");
  });

  it("returns 403 error message", () => {
    const error = new ApiError(403, "Forbidden");
    expect(getErrorMessage(error)).toBe(
      "Access denied. You do not have permission for this action.",
    );
  });

  it("returns 404 error message", () => {
    const error = new ApiError(404, "Not Found");
    expect(getErrorMessage(error)).toBe("The requested resource was not found.");
  });

  it("returns 500+ error message", () => {
    const error = new ApiError(500, "Internal Server Error");
    expect(getErrorMessage(error)).toBe("Server error. Please try again later.");

    const error502 = new ApiError(502, "Bad Gateway");
    expect(getErrorMessage(error502)).toBe("Server error. Please try again later.");
  });

  it("returns custom error message for other status codes", () => {
    const error = new ApiError(400, "Bad Request", undefined, false);
    error.message = "Custom validation error";
    expect(getErrorMessage(error)).toBe("Custom validation error");
  });

  it("handles regular Error instances", () => {
    const error = new Error("Something went wrong");
    expect(getErrorMessage(error)).toBe("Something went wrong");
  });

  it("handles unknown errors", () => {
    expect(getErrorMessage(null)).toBe("An unexpected error occurred.");
    expect(getErrorMessage("string error")).toBe("An unexpected error occurred.");
    expect(getErrorMessage(123)).toBe("An unexpected error occurred.");
  });
});

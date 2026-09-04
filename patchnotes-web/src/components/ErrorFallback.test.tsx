import { render, screen } from "../test/utils";
import userEvent from "@testing-library/user-event";
import { ErrorFallback } from "./ErrorFallback";

describe("ErrorFallback", () => {
  const mockResetErrorBoundary = vi.fn();

  beforeEach(() => {
    mockResetErrorBoundary.mockClear();
  });

  it("renders error message heading", () => {
    render(
      <ErrorFallback error={new Error("Test error")} resetErrorBoundary={mockResetErrorBoundary} />,
    );

    expect(screen.getByText("Something went wrong")).toBeInTheDocument();
  });

  it("renders description text", () => {
    render(
      <ErrorFallback error={new Error("Test error")} resetErrorBoundary={mockResetErrorBoundary} />,
    );

    expect(screen.getByText("An unexpected error occurred. Please try again.")).toBeInTheDocument();
  });

  it("displays error message in details section", () => {
    render(
      <ErrorFallback
        error={new Error("Specific error message")}
        resetErrorBoundary={mockResetErrorBoundary}
      />,
    );

    expect(screen.getByText("Specific error message")).toBeInTheDocument();
  });

  it("handles non-Error objects", () => {
    const nonError = "string error" as unknown as Error;
    render(<ErrorFallback error={nonError} resetErrorBoundary={mockResetErrorBoundary} />);

    expect(screen.getByText("An unknown error occurred")).toBeInTheDocument();
  });

  it("renders try again button", () => {
    render(
      <ErrorFallback error={new Error("Test error")} resetErrorBoundary={mockResetErrorBoundary} />,
    );

    expect(screen.getByRole("button", { name: "Try again" })).toBeInTheDocument();
  });

  it("calls resetErrorBoundary when try again is clicked", async () => {
    const user = userEvent.setup();
    render(
      <ErrorFallback error={new Error("Test error")} resetErrorBoundary={mockResetErrorBoundary} />,
    );

    await user.click(screen.getByRole("button", { name: "Try again" }));

    expect(mockResetErrorBoundary).toHaveBeenCalledTimes(1);
  });

  it("has expandable error details", () => {
    render(
      <ErrorFallback error={new Error("Test error")} resetErrorBoundary={mockResetErrorBoundary} />,
    );

    const details = screen.getByText("Error details");
    expect(details.tagName).toBe("SUMMARY");
  });
});

import { render, screen } from "../../test/utils";
import { Badge } from "./Badge";

describe("Badge", () => {
  it("renders children correctly", () => {
    render(<Badge>v1.0.0</Badge>);
    expect(screen.getByText("v1.0.0")).toBeInTheDocument();
  });

  it("applies default variant styles", () => {
    render(<Badge>Default</Badge>);
    const badge = screen.getByText("Default");
    expect(badge.className).toContain("bg-surface-tertiary");
    expect(badge.className).toContain("text-text-secondary");
  });

  it("applies major variant styles", () => {
    render(<Badge variant="major">Major</Badge>);
    const badge = screen.getByText("Major");
    expect(badge.className).toContain("bg-major-muted");
    expect(badge.className).toContain("text-major");
  });

  it("applies minor variant styles", () => {
    render(<Badge variant="minor">Minor</Badge>);
    const badge = screen.getByText("Minor");
    expect(badge.className).toContain("bg-minor-muted");
    expect(badge.className).toContain("text-minor");
  });

  it("applies patch variant styles", () => {
    render(<Badge variant="patch">Patch</Badge>);
    const badge = screen.getByText("Patch");
    expect(badge.className).toContain("bg-patch-muted");
    expect(badge.className).toContain("text-patch");
  });

  it("applies prerelease variant styles", () => {
    render(<Badge variant="prerelease">Beta</Badge>);
    const badge = screen.getByText("Beta");
    expect(badge.className).toContain("bg-prerelease-muted");
    expect(badge.className).toContain("text-prerelease");
  });

  it("applies custom className", () => {
    render(<Badge className="custom-badge">Custom</Badge>);
    expect(screen.getByText("Custom").className).toContain("custom-badge");
  });

  it("passes additional props", () => {
    render(<Badge data-testid="test-badge">Props</Badge>);
    expect(screen.getByTestId("test-badge")).toBeInTheDocument();
  });

  it("forwards ref correctly", () => {
    const ref = vi.fn();
    render(<Badge ref={ref}>With Ref</Badge>);
    expect(ref).toHaveBeenCalledWith(expect.any(HTMLSpanElement));
  });
});

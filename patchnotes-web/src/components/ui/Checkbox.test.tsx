import { render, screen } from "../../test/utils";
import userEvent from "@testing-library/user-event";
import { Checkbox } from "./Checkbox";

describe("Checkbox", () => {
  it("renders a checkbox input", () => {
    render(<Checkbox />);
    expect(screen.getByRole("checkbox")).toBeInTheDocument();
  });

  it("renders with label", () => {
    render(<Checkbox label="Accept terms" />);
    expect(screen.getByLabelText("Accept terms")).toBeInTheDocument();
    expect(screen.getByText("Accept terms")).toBeInTheDocument();
  });

  it("renders with description", () => {
    render(<Checkbox label="Newsletter" description="Receive weekly updates" />);
    expect(screen.getByText("Receive weekly updates")).toBeInTheDocument();
  });

  it("generates id from label", () => {
    render(<Checkbox label="Agree to Policy" />);
    const checkbox = screen.getByRole("checkbox");
    expect(checkbox).toHaveAttribute("id", "agree-to-policy");
  });

  it("uses provided id over generated one", () => {
    render(<Checkbox label="Accept" id="custom-checkbox" />);
    const checkbox = screen.getByRole("checkbox");
    expect(checkbox).toHaveAttribute("id", "custom-checkbox");
  });

  it("handles click to toggle", async () => {
    const user = userEvent.setup();
    const handleChange = vi.fn();
    render(<Checkbox label="Toggle me" onChange={handleChange} />);

    const checkbox = screen.getByRole("checkbox");
    expect(checkbox).not.toBeChecked();

    await user.click(checkbox);
    expect(checkbox).toBeChecked();
    expect(handleChange).toHaveBeenCalled();
  });

  it("can be pre-checked", () => {
    render(<Checkbox label="Checked" checked onChange={() => {}} />);
    expect(screen.getByRole("checkbox")).toBeChecked();
  });

  it("can be disabled", () => {
    render(<Checkbox label="Disabled" disabled />);
    expect(screen.getByRole("checkbox")).toBeDisabled();
  });

  it("applies disabled styles to wrapper", () => {
    render(<Checkbox label="Disabled" disabled />);
    const label = screen.getByText("Disabled").closest("label");
    expect(label?.className).toContain("opacity-50");
  });

  it("applies custom className", () => {
    render(<Checkbox className="custom-checkbox" />);
    const label = screen.getByRole("checkbox").closest("label");
    expect(label?.className).toContain("custom-checkbox");
  });

  it("forwards ref correctly", () => {
    const ref = vi.fn();
    render(<Checkbox ref={ref} />);
    expect(ref).toHaveBeenCalledWith(expect.any(HTMLInputElement));
  });
});

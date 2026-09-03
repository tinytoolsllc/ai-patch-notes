import { render, screen, waitFor } from "../../test/utils";
import userEvent from "@testing-library/user-event";
import { PackagePicker } from "./PackagePicker";

const mockPackages = [
  {
    id: "pkg-1",
    npmName: "react",
    githubOwner: "facebook",
    githubRepo: "react",
  },
  {
    id: "pkg-2",
    npmName: "lodash",
    githubOwner: "lodash",
    githubRepo: "lodash",
  },
  {
    id: "pkg-3",
    npmName: "typescript",
    githubOwner: "microsoft",
    githubRepo: "TypeScript",
  },
];

describe("PackagePicker", () => {
  beforeEach(() => {
    localStorage.clear();
  });

  describe("rendering", () => {
    it("renders header with title", () => {
      render(<PackagePicker packages={mockPackages} />);
      expect(screen.getByText("Packages")).toBeInTheDocument();
    });

    it("renders loading skeletons when isLoading", () => {
      render(<PackagePicker packages={[]} isLoading />);
      const skeletons = document.querySelectorAll(".animate-pulse");
      expect(skeletons.length).toBeGreaterThan(0);
    });

    it("renders empty state when no packages", () => {
      render(<PackagePicker packages={[]} />);
      expect(screen.getByText("No packages tracked yet")).toBeInTheDocument();
    });

    it("renders all packages", () => {
      render(<PackagePicker packages={mockPackages} />);
      expect(screen.getByText("react")).toBeInTheDocument();
      expect(screen.getByText("lodash")).toBeInTheDocument();
      expect(screen.getByText("typescript")).toBeInTheDocument();
    });

    it("renders package github info as description", () => {
      render(<PackagePicker packages={mockPackages} />);
      expect(screen.getByText("facebook/react")).toBeInTheDocument();
      expect(screen.getByText("lodash/lodash")).toBeInTheDocument();
    });

    it("shows selection count", () => {
      render(<PackagePicker packages={mockPackages} />);
      expect(screen.getByText("0 of 3 selected")).toBeInTheDocument();
    });
  });

  describe("selection", () => {
    it("selects a package on click", async () => {
      const user = userEvent.setup();
      const onSelectionChange = vi.fn();
      render(<PackagePicker packages={mockPackages} onSelectionChange={onSelectionChange} />);

      await user.click(screen.getByRole("checkbox", { name: /react/i }));

      await waitFor(() => {
        expect(onSelectionChange).toHaveBeenCalledWith(["pkg-1"]);
      });
    });

    it("deselects a package on second click", async () => {
      const user = userEvent.setup();
      const onSelectionChange = vi.fn();
      render(<PackagePicker packages={mockPackages} onSelectionChange={onSelectionChange} />);

      const checkbox = screen.getByRole("checkbox", { name: /react/i });
      await user.click(checkbox);
      await user.click(checkbox);

      await waitFor(() => {
        expect(onSelectionChange).toHaveBeenLastCalledWith([]);
      });
    });

    it("selects all packages with select all button", async () => {
      const user = userEvent.setup();
      const onSelectionChange = vi.fn();
      render(<PackagePicker packages={mockPackages} onSelectionChange={onSelectionChange} />);

      await user.click(screen.getByText("Select all"));

      await waitFor(() => {
        expect(onSelectionChange).toHaveBeenCalledWith(["pkg-1", "pkg-2", "pkg-3"]);
      });
    });

    it("deselects all when all are selected", async () => {
      const user = userEvent.setup();
      const onSelectionChange = vi.fn();
      render(<PackagePicker packages={mockPackages} onSelectionChange={onSelectionChange} />);

      // Select all first
      await user.click(screen.getByText("Select all"));

      // Button should now show deselect
      await user.click(screen.getByText("Deselect all"));

      await waitFor(() => {
        expect(onSelectionChange).toHaveBeenLastCalledWith([]);
      });
    });

    it("updates selection count", async () => {
      const user = userEvent.setup();
      render(<PackagePicker packages={mockPackages} />);

      expect(screen.getByText("0 of 3 selected")).toBeInTheDocument();

      await user.click(screen.getByRole("checkbox", { name: /react/i }));
      await waitFor(() => {
        expect(screen.getByText("1 of 3 selected")).toBeInTheDocument();
      });

      await user.click(screen.getByRole("checkbox", { name: /lodash/i }));
      await waitFor(() => {
        expect(screen.getByText("2 of 3 selected")).toBeInTheDocument();
      });
    });
  });

  describe("localStorage persistence", () => {
    it("persists selection to localStorage", async () => {
      const user = userEvent.setup();
      render(<PackagePicker packages={mockPackages} storageKey="test" />);

      await user.click(screen.getByRole("checkbox", { name: /react/i }));

      await waitFor(() => {
        const stored = localStorage.getItem("patchnotes:package-selection:test");
        expect(stored).toBe('["pkg-1"]');
      });
    });

    it("restores selection from localStorage", () => {
      localStorage.setItem("patchnotes:package-selection:test", '["pkg-1", "pkg-2"]');

      render(<PackagePicker packages={mockPackages} storageKey="test" />);

      expect(screen.getByRole("checkbox", { name: /react/i })).toBeChecked();
      expect(screen.getByRole("checkbox", { name: /lodash/i })).toBeChecked();
      expect(screen.getByRole("checkbox", { name: /typescript/i })).not.toBeChecked();
    });
  });

  describe("add package", () => {
    it("renders add package input when onAddPackage provided", () => {
      render(<PackagePicker packages={mockPackages} onAddPackage={async () => {}} />);
      expect(screen.getByPlaceholderText("Add package (e.g., lodash)")).toBeInTheDocument();
    });

    it("does not render add package input when onAddPackage not provided", () => {
      render(<PackagePicker packages={mockPackages} />);
      expect(screen.queryByPlaceholderText("Add package (e.g., lodash)")).not.toBeInTheDocument();
    });

    it("calls onAddPackage with input value", async () => {
      const user = userEvent.setup();
      const onAddPackage = vi.fn().mockResolvedValue(undefined);
      render(<PackagePicker packages={mockPackages} onAddPackage={onAddPackage} />);

      const input = screen.getByPlaceholderText("Add package (e.g., lodash)");
      await user.type(input, "axios");
      await user.click(screen.getByRole("button", { name: "Add" }));

      expect(onAddPackage).toHaveBeenCalledWith("axios");
    });

    it("clears input after successful add", async () => {
      const user = userEvent.setup();
      const onAddPackage = vi.fn().mockResolvedValue(undefined);
      render(<PackagePicker packages={mockPackages} onAddPackage={onAddPackage} />);

      const input = screen.getByPlaceholderText("Add package (e.g., lodash)");
      await user.type(input, "axios");
      await user.click(screen.getByRole("button", { name: "Add" }));

      await waitFor(() => {
        expect(input).toHaveValue("");
      });
    });

    it("submits on Enter key", async () => {
      const user = userEvent.setup();
      const onAddPackage = vi.fn().mockResolvedValue(undefined);
      render(<PackagePicker packages={mockPackages} onAddPackage={onAddPackage} />);

      const input = screen.getByPlaceholderText("Add package (e.g., lodash)");
      await user.type(input, "axios{enter}");

      expect(onAddPackage).toHaveBeenCalledWith("axios");
    });

    it("disables add button when input is empty", () => {
      render(<PackagePicker packages={mockPackages} onAddPackage={async () => {}} />);
      expect(screen.getByRole("button", { name: "Add" })).toBeDisabled();
    });

    it("trims whitespace from package name", async () => {
      const user = userEvent.setup();
      const onAddPackage = vi.fn().mockResolvedValue(undefined);
      render(<PackagePicker packages={mockPackages} onAddPackage={onAddPackage} />);

      const input = screen.getByPlaceholderText("Add package (e.g., lodash)");
      await user.type(input, "  axios  ");
      await user.click(screen.getByRole("button", { name: "Add" }));

      expect(onAddPackage).toHaveBeenCalledWith("axios");
    });
  });
});

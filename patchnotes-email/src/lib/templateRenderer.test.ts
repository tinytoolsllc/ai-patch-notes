import { describe, it, expect } from "vitest";
import { renderTemplate, interpolateSubject } from "./templateRenderer";

describe("interpolateSubject", () => {
  it("replaces {{variable}} placeholders", () => {
    expect(interpolateSubject("Hello, {{name}}!", { name: "Alice" })).toBe("Hello, Alice!");
  });

  it("replaces multiple placeholders", () => {
    expect(interpolateSubject("{{name}} has {{count}} updates", { name: "Bob", count: "3" })).toBe(
      "Bob has 3 updates",
    );
  });

  it("replaces missing variables with empty string", () => {
    expect(interpolateSubject("Hello, {{name}}!", {})).toBe("Hello, !");
  });

  it("returns string unchanged when no placeholders", () => {
    expect(interpolateSubject("No placeholders here", { name: "Alice" })).toBe(
      "No placeholders here",
    );
  });
});

describe("renderTemplate", () => {
  it("renders a simple JSX component to HTML", async () => {
    const jsxSource = `
            import * as React from "react";

            export default function TestEmail({ name }) {
                return React.createElement("div", null, "Hello, " + name + "!");
            }
        `;

    const html = await renderTemplate(jsxSource, { name: "Alice" });
    expect(html).toContain("Hello, Alice!");
  });

  it("renders a component using React Email components", async () => {
    const jsxSource = `
            import { Html, Text } from "@react-email/components";

            export default function TestEmail({ name }) {
                return <Html><Text>Welcome, {name}!</Text></Html>;
            }
        `;

    const html = await renderTemplate(jsxSource, { name: "Bob" });
    expect(html).toContain("Welcome,");
    expect(html).toContain("Bob");
  });

  it("throws when template does not export a component", async () => {
    const jsxSource = `export const foo = "bar";`;

    await expect(renderTemplate(jsxSource, {})).rejects.toThrow(
      "Template does not export a valid React component",
    );
  });

  it("throws on invalid JSX syntax", async () => {
    const jsxSource = `export default function() { return <<<<; }`;

    await expect(renderTemplate(jsxSource, {})).rejects.toThrow();
  });
});

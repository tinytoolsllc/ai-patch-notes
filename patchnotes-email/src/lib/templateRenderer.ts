import { transform } from "esbuild-wasm";
import { render } from "@react-email/render";
import * as React from "react";
import * as ReactEmailComponents from "@react-email/components";

const MODULE_MAP: Record<string, unknown> = {
  react: React,
  "@react-email/components": ReactEmailComponents,
};

function sandboxRequire(moduleName: string): unknown {
  const mod = MODULE_MAP[moduleName];
  if (!mod) throw new Error(`Module not available in template sandbox: ${moduleName}`);
  return mod;
}

/**
 * Transpiles JSX source from the database into HTML using React Email's render().
 * The JSX source should export a default React component function.
 *
 * Security note: Templates are admin-seeded in the database via the admin API,
 * not from user input. The sandboxed require only exposes react and
 * @react-email/components modules.
 */
export async function renderTemplate(
  jsxSource: string,
  props: Record<string, unknown>,
): Promise<string> {
  const { code } = await transform(jsxSource, {
    loader: "tsx",
    jsx: "transform",
    jsxFactory: "React.createElement",
    jsxFragment: "React.Fragment",
    format: "cjs",
  });

  const moduleObj: { exports: Record<string, unknown> } = { exports: {} };
  const fn = new Function("module", "exports", "require", "React", code);
  fn(moduleObj, moduleObj.exports, sandboxRequire, React);

  const Component = (moduleObj.exports.default ?? moduleObj.exports) as React.FC;
  if (typeof Component !== "function") {
    throw new Error("Template does not export a valid React component");
  }

  return await render(React.createElement(Component, props));
}

/**
 * Interpolates {{variable}} placeholders in a subject line template.
 */
export function interpolateSubject(subject: string, vars: Record<string, string>): string {
  return subject.replace(/\{\{(\w+)\}\}/g, (_, key) => vars[key] ?? "");
}

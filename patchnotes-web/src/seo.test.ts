import { describe, it, expect } from "vitest";
import { seoHead } from "./seo";

describe("seoHead", () => {
  const defaultOpts = {
    title: "Test Page | My Release Notes",
    description: "A test description.",
    path: "/test",
  };

  it("returns correct title in meta array", () => {
    const { meta } = seoHead(defaultOpts);
    expect(meta).toContainEqual({ title: "Test Page | My Release Notes" });
  });

  it("returns correct description meta tag", () => {
    const { meta } = seoHead(defaultOpts);
    expect(meta).toContainEqual({
      name: "description",
      content: "A test description.",
    });
  });

  it("returns canonical link with full URL", () => {
    const { links } = seoHead(defaultOpts);
    expect(links).toContainEqual({
      rel: "canonical",
      href: "https://www.myreleasenotes.ai/test",
    });
  });

  it("includes OG and Twitter tags", () => {
    const { meta } = seoHead(defaultOpts);
    expect(meta).toContainEqual({
      property: "og:title",
      content: defaultOpts.title,
    });
    expect(meta).toContainEqual({
      property: "og:description",
      content: defaultOpts.description,
    });
    expect(meta).toContainEqual({
      property: "og:url",
      content: "https://www.myreleasenotes.ai/test",
    });
    expect(meta).toContainEqual({ property: "og:type", content: "website" });
    expect(meta).toContainEqual({
      property: "og:image",
      content: "https://www.myreleasenotes.ai/og-image.png",
    });
    expect(meta).toContainEqual({
      name: "twitter:card",
      content: "summary_large_image",
    });
    expect(meta).toContainEqual({
      name: "twitter:title",
      content: defaultOpts.title,
    });
    expect(meta).toContainEqual({
      name: "twitter:description",
      content: defaultOpts.description,
    });
    expect(meta).toContainEqual({
      name: "twitter:image",
      content: "https://www.myreleasenotes.ai/og-image.png",
    });
  });

  it("adds robots noindex when noindex is true", () => {
    const { meta } = seoHead({ ...defaultOpts, noindex: true });
    expect(meta).toContainEqual({ name: "robots", content: "noindex" });
  });

  it("omits robots tag when noindex is false", () => {
    const { meta } = seoHead({ ...defaultOpts, noindex: false });
    expect(meta.find((m) => "name" in m && m.name === "robots")).toBeUndefined();
  });

  it("omits robots tag when noindex is undefined", () => {
    const { meta } = seoHead(defaultOpts);
    expect(meta.find((m) => "name" in m && m.name === "robots")).toBeUndefined();
  });

  it("includes JSON-LD script tag when jsonLd is provided", () => {
    const jsonLd = {
      "@context": "https://schema.org",
      "@type": "WebApplication",
      name: "Test App",
    };
    const { meta } = seoHead({ ...defaultOpts, jsonLd });
    expect(meta).toContainEqual({ "script:ld+json": jsonLd });
  });

  it("omits JSON-LD when jsonLd is not provided", () => {
    const { meta } = seoHead(defaultOpts);
    expect(meta.find((m) => "script:ld+json" in m)).toBeUndefined();
  });
});

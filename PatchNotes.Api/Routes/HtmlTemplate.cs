using System.Text;
using System.Web;

namespace PatchNotes.Api.Routes;

public static class HtmlTemplate
{
    private const string BaseUrl = "https://www.myreleasenotes.ai";

    private const string ThemeScript = """
        <script>
        try{var d=JSON.parse(localStorage.getItem('patchnotes-theme'));var t=d&&d.state&&d.state.theme;var r=t==='dark'?'dark':t==='light'?'light':window.matchMedia('(prefers-color-scheme: dark)').matches?'dark':'light';document.documentElement.classList.add(r)}catch(e){}
        </script>
        """;

    private const string Css = """
        :root {
          --color-brand-500: oklch(0.55 0.17 250);
          --color-brand-600: oklch(0.48 0.18 250);
          --color-brand-700: oklch(0.42 0.16 250);
          --color-major: oklch(0.65 0.2 25);
          --color-major-muted: oklch(0.92 0.04 25);
          --color-minor: oklch(0.72 0.16 145);
          --color-minor-muted: oklch(0.94 0.04 145);
          --color-patch: oklch(0.7 0.14 230);
          --color-patch-muted: oklch(0.94 0.03 230);
          --color-prerelease: oklch(0.7 0.15 300);
          --color-prerelease-muted: oklch(0.94 0.03 300);
          --color-surface-primary: oklch(1 0 0);
          --color-surface-secondary: oklch(0.98 0 0);
          --color-surface-tertiary: oklch(0.96 0 0);
          --color-text-primary: oklch(0.2 0.02 250);
          --color-text-secondary: oklch(0.45 0.02 250);
          --color-text-tertiary: oklch(0.6 0.01 250);
          --color-border-default: oklch(0.9 0.01 250);
          --font-sans: 'Inter', ui-sans-serif, system-ui, sans-serif;
          --font-mono: 'JetBrains Mono', ui-monospace, monospace;
        }
        :root.dark {
          --color-surface-primary: oklch(0.16 0.02 250);
          --color-surface-secondary: oklch(0.2 0.02 250);
          --color-surface-tertiary: oklch(0.24 0.02 250);
          --color-text-primary: oklch(0.95 0 0);
          --color-text-secondary: oklch(0.7 0.01 250);
          --color-text-tertiary: oklch(0.55 0.01 250);
          --color-border-default: oklch(0.3 0.02 250);
          --color-major-muted: oklch(0.3 0.08 25);
          --color-minor-muted: oklch(0.28 0.06 145);
          --color-patch-muted: oklch(0.28 0.05 230);
          --color-prerelease-muted: oklch(0.28 0.06 300);
        }
        *, *::before, *::after { margin: 0; padding: 0; box-sizing: border-box; }
        html { font-family: var(--font-sans); -webkit-font-smoothing: antialiased; }
        body { background: var(--color-surface-secondary); color: var(--color-text-primary); min-height: 100vh; display: flex; flex-direction: column; }
        a { color: var(--color-brand-600); text-decoration: none; }
        a:hover { color: var(--color-brand-700); text-decoration: underline; }

        .header { position: sticky; top: 0; z-index: 40; background: color-mix(in oklch, var(--color-surface-primary) 80%, transparent); backdrop-filter: blur(12px); border-bottom: 1px solid var(--color-border-default); }
        .header-inner { max-width: 72rem; margin: 0 auto; padding: 0 1.5rem; height: 4rem; display: flex; align-items: center; justify-content: space-between; }
        .header-left { display: flex; align-items: center; gap: 0.375rem; }
        .logo-link { display: flex; align-items: center; gap: 0.625rem; text-decoration: none; }
        .logo-link:hover { opacity: 0.8; text-decoration: none; }
        .logo-text { font-weight: 600; font-size: 1.25rem; color: var(--color-text-primary); }
        .logo-sub { font-size: 0.625rem; color: var(--color-text-tertiary); line-height: 1.1; }
        .breadcrumb-sep { color: var(--color-text-tertiary); margin: 0 0.25rem; }
        .breadcrumb { color: var(--color-text-secondary); text-decoration: none; font-size: 0.875rem; }
        .breadcrumb:hover { color: var(--color-text-primary); text-decoration: none; }
        .breadcrumb-current { color: var(--color-text-primary); font-weight: 600; font-size: 0.875rem; }
        .sign-in { display: inline-flex; align-items: center; gap: 0.5rem; padding: 0.375rem 0.75rem; font-size: 0.875rem; font-weight: 500; color: var(--color-text-primary); background: var(--color-surface-primary); border: 1px solid var(--color-border-default); border-radius: 0.5rem; text-decoration: none; transition: background 0.15s; }
        .sign-in:hover { background: var(--color-surface-tertiary); text-decoration: none; }

        .container { max-width: 72rem; margin: 0 auto; padding: 0 1.5rem; }
        main { padding: 2rem 0; flex: 1; }

        .card { background: var(--color-surface-primary); border: 1px solid var(--color-border-default); border-radius: 0.5rem; box-shadow: 0 1px 2px 0 oklch(0 0 0 / 0.05); }
        .card-padded { padding: 1.5rem; }
        .card + .card { margin-top: 1rem; }

        .badge { display: inline-flex; align-items: center; padding: 0.125rem 0.5rem; border-radius: 9999px; font-size: 0.75rem; font-weight: 500; }
        .badge-major { background: var(--color-major-muted); color: var(--color-major); }
        .badge-minor { background: var(--color-minor-muted); color: var(--color-minor); }
        .badge-patch { background: var(--color-patch-muted); color: var(--color-patch); }
        .badge-prerelease { background: var(--color-prerelease-muted); color: var(--color-prerelease); }

        .text-secondary { color: var(--color-text-secondary); }
        .text-tertiary { color: var(--color-text-tertiary); }
        .text-sm { font-size: 0.875rem; }
        .text-xs { font-size: 0.75rem; }
        .text-lg { font-size: 1.125rem; }
        .text-xl { font-size: 1.25rem; }
        .font-semibold { font-weight: 600; }
        .mt-1 { margin-top: 0.25rem; }
        .mt-2 { margin-top: 0.5rem; }
        .mt-4 { margin-top: 1rem; }
        .mb-2 { margin-bottom: 0.5rem; }
        .mb-4 { margin-bottom: 1rem; }
        .mb-8 { margin-bottom: 2rem; }
        .space-y-4 > * + * { margin-top: 1rem; }
        .flex { display: flex; }
        .items-center { align-items: center; }
        .gap-2 { gap: 0.5rem; }
        .gap-3 { gap: 0.75rem; }
        .gap-6 { gap: 1.5rem; }
        .flex-wrap { flex-wrap: wrap; }

        .group-header { padding: 1rem 1.5rem; display: flex; align-items: center; justify-content: space-between; gap: 1rem; }
        .group-title { font-weight: 600; font-size: 0.875rem; }
        .group-summary { padding: 0 1.5rem 1rem; font-size: 0.875rem; color: var(--color-text-secondary); line-height: 1.5; }
        .group-meta { font-size: 0.75rem; color: var(--color-text-tertiary); }

        .release-list { border-top: 1px solid var(--color-border-default); }
        .release-item { padding: 0.75rem 1.5rem; display: flex; align-items: center; justify-content: space-between; border-bottom: 1px solid var(--color-border-default); }
        .release-item:last-child { border-bottom: none; }

        .pkg-list-item { padding: 1rem 1.5rem; display: flex; align-items: center; justify-content: space-between; border-bottom: 1px solid var(--color-border-default); }
        .pkg-list-item:last-child { border-bottom: none; }

        .prose-release { line-height: 1.65; }
        .prose-release h1, .prose-release h2, .prose-release h3 { font-weight: 600; margin-top: 1.5em; margin-bottom: 0.5em; }
        .prose-release h1 { font-size: 1.25rem; }
        .prose-release h2 { font-size: 1.125rem; }
        .prose-release h3 { font-size: 1rem; }
        .prose-release p { margin-bottom: 1em; }
        .prose-release ul, .prose-release ol { padding-left: 1.5em; margin-bottom: 1em; }
        .prose-release li { margin-bottom: 0.25em; }
        .prose-release code { font-family: var(--font-mono); font-size: 0.875em; background: var(--color-surface-tertiary); padding: 0.125em 0.375em; border-radius: 0.25rem; }
        .prose-release pre { background: var(--color-surface-tertiary); padding: 1em; border-radius: 0.375rem; overflow-x: auto; margin-bottom: 1em; }
        .prose-release pre code { background: none; padding: 0; }
        .prose-release a { color: var(--color-brand-600); text-decoration: underline; text-underline-offset: 2px; }
        .prose-release a:hover { color: var(--color-brand-700); }

        footer { margin-top: auto; background: color-mix(in oklch, var(--color-surface-primary) 80%, transparent); backdrop-filter: blur(12px); border-top: 1px solid var(--color-border-default); padding: 1.5rem; text-align: center; font-size: 0.875rem; color: var(--color-text-tertiary); }
        footer a { color: var(--color-text-secondary); text-decoration: none; }
        footer a:hover { color: var(--color-text-primary); }
        footer nav { display: flex; justify-content: center; gap: 1.5rem; margin-bottom: 0.75rem; }

        .spinner-container { min-height: 80vh; display: flex; align-items: center; justify-content: center; }
        .spinner { width: 2rem; height: 2rem; border: 3px solid var(--color-brand-500); border-top-color: transparent; border-radius: 50%; animation: spin 0.8s linear infinite; }
        @keyframes spin { to { transform: rotate(360deg); } }
        """;

    public static string Wrap(string title, string description, string path, string bodyHtml, string? jsonLd = null)
    {
        var sb = new StringBuilder(8192);
        sb.Append("<!DOCTYPE html><html lang=\"en\"><head>");
        sb.Append("<meta charset=\"UTF-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.Append($"<title>{Encode(title)}</title>");
        sb.Append($"<meta name=\"description\" content=\"{Encode(description)}\">");

        // Open Graph
        sb.Append($"<meta property=\"og:title\" content=\"{Encode(title)}\">");
        sb.Append($"<meta property=\"og:description\" content=\"{Encode(description)}\">");
        sb.Append($"<meta property=\"og:url\" content=\"{BaseUrl}{Encode(path)}\">");
        sb.Append("<meta property=\"og:type\" content=\"website\">");

        // Canonical
        sb.Append($"<link rel=\"canonical\" href=\"{BaseUrl}{Encode(path)}\">");

        // Fonts
        sb.Append("<link rel=\"preconnect\" href=\"https://fonts.googleapis.com\">");
        sb.Append("<link rel=\"preconnect\" href=\"https://fonts.gstatic.com\" crossorigin>");
        sb.Append("<link href=\"https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap\" rel=\"stylesheet\">");

        sb.Append(ThemeScript);
        sb.Append("<style>");
        sb.Append(Css);
        sb.Append("</style>");

        if (jsonLd != null)
        {
            sb.Append($"<script type=\"application/ld+json\">{jsonLd}</script>");
        }

        sb.Append("</head><body>");
        sb.Append(bodyHtml);
        sb.Append(Footer());
        sb.Append("</body></html>");
        return sb.ToString();
    }

    public static string WrapAuthenticated(string title, string path, string preloadedDataJson, string spaBaseUrl)
    {
        var sb = new StringBuilder(4096);
        sb.Append("<!DOCTYPE html><html lang=\"en\"><head>");
        sb.Append("<meta charset=\"UTF-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.Append($"<title>{Encode(title)}</title>");

        // Fonts
        sb.Append("<link rel=\"preconnect\" href=\"https://fonts.googleapis.com\">");
        sb.Append("<link rel=\"preconnect\" href=\"https://fonts.gstatic.com\" crossorigin>");
        sb.Append("<link href=\"https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap\" rel=\"stylesheet\">");

        sb.Append(ThemeScript);
        sb.Append("<style>");
        sb.Append(Css);
        sb.Append("</style>");

        sb.Append("</head><body>");
        sb.Append("<div class=\"spinner-container\"><div class=\"spinner\"></div></div>");
        sb.Append($"<script>window.__PRELOADED_DATA__={preloadedDataJson};</script>");
        // Redirect to the SPA at the same path
        sb.Append($"<script>window.location.replace(\"{Encode(spaBaseUrl)}{Encode(path)}\");</script>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private const string LogoSvg = """
        <svg width="36" height="36" viewBox="0 0 64 64" fill="none" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">
          <rect x="10" y="8" width="44" height="52" rx="4" fill="oklch(0.93 0.02 250)" stroke="oklch(0.48 0.18 250)" stroke-width="2"/>
          <circle cx="18" cy="8" r="2" fill="oklch(0.48 0.18 250)"/>
          <circle cx="28" cy="8" r="2" fill="oklch(0.48 0.18 250)"/>
          <circle cx="38" cy="8" r="2" fill="oklch(0.48 0.18 250)"/>
          <circle cx="48" cy="8" r="2" fill="oklch(0.48 0.18 250)"/>
          <path d="M24 30L32 26L40 30L40 40L32 44L24 40Z" fill="oklch(0.86 0.04 250)" stroke="oklch(0.48 0.18 250)" stroke-width="1.5" stroke-linejoin="round"/>
          <path d="M24 30L32 26L40 30L32 34Z" fill="oklch(0.76 0.08 250)" stroke="oklch(0.48 0.18 250)" stroke-width="1.5" stroke-linejoin="round"/>
          <line x1="32" y1="34" x2="32" y2="44" stroke="oklch(0.48 0.18 250)" stroke-width="1.5"/>
          <line x1="18" y1="50" x2="32" y2="50" stroke="oklch(0.64 0.13 250)" stroke-width="1.5" stroke-linecap="round"/>
          <line x1="18" y1="54" x2="28" y2="54" stroke="oklch(0.76 0.08 250)" stroke-width="1.5" stroke-linecap="round"/>
        </svg>
        """;

    public static string Header(params (string Text, string? Href)[] breadcrumbs)
    {
        var sb = new StringBuilder(1024);
        sb.Append("<header class=\"header\"><div class=\"header-inner\">");

        // Left side: logo + breadcrumbs
        sb.Append("<div class=\"header-left\">");
        sb.Append($"<a href=\"{BaseUrl}\" class=\"logo-link\">");
        sb.Append(LogoSvg);
        sb.Append("<div><p class=\"logo-text\">My Release Notes</p><p class=\"logo-sub\">by Tiny Tools</p></div>");
        sb.Append("</a>");

        foreach (var (text, href) in breadcrumbs)
        {
            sb.Append("<span class=\"breadcrumb-sep\">/</span>");
            if (href != null)
            {
                sb.Append($"<a href=\"{Encode(href)}\" class=\"breadcrumb\">{Encode(text)}</a>");
            }
            else
            {
                sb.Append($"<span class=\"breadcrumb-current\">{Encode(text)}</span>");
            }
        }
        sb.Append("</div>");

        // Right side: sign in
        sb.Append($"<a href=\"{BaseUrl}/login\" class=\"sign-in\">Sign in</a>");

        sb.Append("</div></header>");
        return sb.ToString();
    }

    public static string Footer()
    {
        return """
            <footer>
                <nav>
                    <a href="https://www.myreleasenotes.ai">Home</a>
                    <a href="https://www.myreleasenotes.ai/pricing">Pricing</a>
                    <a href="https://www.myreleasenotes.ai/about">About</a>
                    <a href="https://www.myreleasenotes.ai/privacy">Privacy</a>
                </nav>
                <span>&copy; 2026 Tiny Tools. Forged in Gas Town.</span>
            </footer>
            """;
    }

    public static string VersionBadge(string tag)
    {
        var cssClass = "badge-patch";
        if (tag.Contains("-alpha") || tag.Contains("-beta") || tag.Contains("-rc") ||
            tag.Contains("-preview") || tag.Contains("-canary") || tag.Contains("-next"))
        {
            cssClass = "badge-prerelease";
        }
        else
        {
            // Parse version to determine badge type
            var version = tag.TrimStart('v');
            var parts = version.Split('.');
            if (parts.Length >= 3 && parts[0] != "0")
            {
                if (parts[1] == "0" && parts[2] == "0") cssClass = "badge-major";
                else if (parts[2] == "0") cssClass = "badge-minor";
            }
        }
        return $"<span class=\"badge {cssClass}\">{Encode(tag)}</span>";
    }

    private static string Encode(string value) => HttpUtility.HtmlEncode(value);
}

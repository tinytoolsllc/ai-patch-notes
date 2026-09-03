import { Resend } from "resend";

const apiKey = process.env.RESEND_API_KEY;
if (!apiKey) {
  throw new Error("RESEND_API_KEY environment variable is required");
}

export const resend = new Resend(apiKey);

export const FROM_ADDRESS = "My Release Notes Digest <digest@myreleasenotes.ai>";

export function sanitizeSubject(str: string): string {
  return str.replace(/[\r\n]+/g, " ").trim();
}

export function escapeHtml(str: string): string {
  return str
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}

/**
 * Basic email format validation. Rejects obviously invalid addresses
 * like "test@", "@foo", or strings with special characters that would
 * fail at the mail provider.
 */
export function isValidEmail(email: string): boolean {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email);
}

export const APP_BASE_URL = process.env.APP_BASE_URL || "https://app.myreleasenotes.ai";

export const SETTINGS_URL = `${APP_BASE_URL}/settings`;

export function emailFooter(): string {
  return `
        <hr style="border: none; border-top: 1px solid #e5e5e5; margin: 32px 0 16px;" />
        <p style="font-size: 12px; color: #666;">
            You're receiving this email because you signed up for PatchNotes.
            <a href="${SETTINGS_URL}">Manage email preferences</a>
        </p>
    `;
}

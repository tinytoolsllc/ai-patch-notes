import { PrismaMssql } from "@prisma/adapter-mssql";
import { PrismaClient } from "../../generated/prisma/client.js";

// Singleton Prisma client for Azure Functions.
// DATABASE_URL is in ADO.NET format (Server=host;Database=name;...)
// but PrismaMssql expects a Prisma-style URL or config object,
// so we parse it into a config object.
let prisma: PrismaClient;

function parseAdoConnectionString(connectionString: string): Record<string, string> {
  const params: Record<string, string> = {};
  for (const part of connectionString.split(";")) {
    const idx = part.indexOf("=");
    if (idx === -1) continue;
    params[part.substring(0, idx).trim().toLowerCase()] = part.substring(idx + 1).trim();
  }
  return params;
}

export function getPrismaClient(): PrismaClient {
  if (!prisma) {
    const connectionString = process.env.DATABASE_URL;
    if (!connectionString) {
      throw new Error("DATABASE_URL environment variable is required");
    }

    const p = parseAdoConnectionString(connectionString);
    const adapter = new PrismaMssql({
      server: p["server"],
      database: p["database"],
      user: p["user id"],
      password: p["password"],
      options: {
        encrypt: p["encrypt"]?.toLowerCase() === "true",
        trustServerCertificate: p["trustservercertificate"]?.toLowerCase() === "true",
      },
    });

    prisma = new PrismaClient({ adapter });
  }
  return prisma;
}

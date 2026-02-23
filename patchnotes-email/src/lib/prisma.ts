import { PrismaMssql } from "@prisma/adapter-mssql";
import { PrismaClient } from "../../generated/prisma/client.js";

// Singleton Prisma client for Azure Functions.
// DATABASE_URL is set via Azure App Settings (production)
// or local.settings.json (local dev).
//
// The connection string should be in mssql package format:
//   Server=host,port;Database=name;User Id=user;Password=pass;Encrypt=true
// OR as a config object via individual env vars (DB_HOST, DB_PORT, etc.)
let prisma: PrismaClient;

export function getPrismaClient(): PrismaClient {
    if (!prisma) {
        const connectionString = process.env.DATABASE_URL;
        if (!connectionString) {
            throw new Error("DATABASE_URL environment variable is required");
        }
        const adapter = new PrismaMssql(connectionString);
        prisma = new PrismaClient({ adapter });
    }
    return prisma;
}

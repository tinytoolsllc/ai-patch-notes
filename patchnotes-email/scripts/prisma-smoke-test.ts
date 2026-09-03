/**
 * Smoke test: verifies Prisma client can connect to the database.
 *
 * Usage:
 *   npx tsx scripts/prisma-smoke-test.ts
 *
 * Requires DATABASE_URL to be set (via .env or environment).
 */
import "dotenv/config";
import { PrismaMssql } from "@prisma/adapter-mssql";
import { PrismaClient } from "../generated/prisma/client";

async function main() {
  const connectionString = process.env.DATABASE_URL;
  if (!connectionString) {
    console.error("DATABASE_URL environment variable is required");
    process.exit(1);
  }

  const adapter = new PrismaMssql(connectionString);
  const prisma = new PrismaClient({ adapter });

  try {
    const result = await prisma.$queryRaw`SELECT 1 AS connected`;
    console.log("Prisma connection successful:", result);
  } catch (err) {
    console.error("Prisma connection FAILED:", err);
    process.exit(1);
  } finally {
    await prisma.$disconnect();
  }
}

main();

import * as logger from "firebase-functions/logger";
import * as admin from "firebase-admin";

export function normalizeId(input: string): string {
  const upper = input.trim().toUpperCase();
  const noDiacritics = upper.normalize("NFD").replace(/[\u0300-\u036f]/g, "");
  const kept = noDiacritics.replace(/[^A-Z0-9\s]/g, " ");
  return kept.replace(/\s+/g, " ").trim();
}

export function normalizeTipo(input: string): "CxC" | "CxP" | null {
  const s = input.trim().toUpperCase().replace(/\s+/g, "");
  if (s === "CXC") return "CxC";
  if (s === "CXP") return "CxP";
  return null;
}

export function parseNumber(value: unknown): number {
  if (typeof value === "number") return value;
  if (typeof value !== "string") return Number.NaN;
  const s = value.trim();
  if (!s) return Number.NaN;
  const normalized = s
    .replace(/\./g, "")
    .replace(/,/g, ".")
    .replace(/^\((.*)\)$/, "-$1");
  const n = Number(normalized);
  return Number.isFinite(n) ? n : Number.NaN;
}

export function parseOptionalInt(value: unknown): number | null {
  if (value === null || value === undefined) return null;
  if (typeof value === "number" && Number.isFinite(value)) return Math.trunc(value);
  if (typeof value === "string") {
    const s = value.trim();
    if (!s) return null;
    const n = Number.parseInt(s, 10);
    return Number.isFinite(n) ? n : null;
  }
  return null;
}

export function round2(n: number): number {
  return Math.round((n + Number.EPSILON) * 100) / 100;
}

export async function loadExistingHashes(
  movimientosCol: admin.firestore.CollectionReference,
): Promise<Set<string>> {
  const hashes = new Set<string>();
  let query: admin.firestore.Query = movimientosCol.orderBy(admin.firestore.FieldPath.documentId()).limit(1000);
  for (;;) {
    const snap = await query.get();
    for (const doc of snap.docs) {
      hashes.add(doc.id);
    }
    if (snap.size < 1000) {
      break;
    }
    query = movimientosCol
      .orderBy(admin.firestore.FieldPath.documentId())
      .startAfter(snap.docs[snap.docs.length - 1]!)
      .limit(1000);
  }
  return hashes;
}

export async function commitBatch(
  db: admin.firestore.Firestore,
  writes: Array<{ ref: admin.firestore.DocumentReference; data: Record<string, unknown> }>,
  onErrorCount: (count: number) => void,
): Promise<void> {
  const batch = db.batch();
  for (const w of writes) {
    batch.create(w.ref, w.data);
  }

  try {
    await batch.commit();
  } catch (err) {
    logger.error("Fallo commit batch", err);
    onErrorCount(writes.length);
  }
}


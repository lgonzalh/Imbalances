import { onRequest } from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import * as admin from "firebase-admin";
import { normalizeId, normalizeTipo, parseNumber, round2 } from "../motor2/utils";

type CruzarRequest = {
  periodo?: string;
  tolerancia?: number | string | null;
};

type CruzarResultado = {
  empresa_a: string;
  empresa_b: string;
  cxC: number;
  cxP: number;
  diferencia: number;
  estado: "OK" | "DESCUADRE";
};

type CruzarResponse = {
  periodo: string;
  tolerancia: number;
  movimientos_leidos: number;
  movimientos_invalidos: number;
  pares_evaluados: number;
  resultados_guardados: number;
  omitidos_ambos_cero: number;
  ok: number;
  descuadre: number;
};

export const cruzar = onRequest(
  {
    region: "southamerica-east1",
    cors: true,
  },
  async (req, res) => {
    if (req.method !== "POST") {
      res.status(405).json({ error: "Use POST" });
      return;
    }

    const body = req.body as CruzarRequest;
    const periodo = (body?.periodo ?? "").trim();
    const toleranciaParsed = parseNumber(body?.tolerancia);
    const tolerancia = Number.isFinite(toleranciaParsed) ? Math.max(0, toleranciaParsed) : 0;

    if (!/^\d{4}-\d{2}$/.test(periodo)) {
      res.status(400).json({ error: "periodo inválido (YYYY-MM)" });
      return;
    }

    const db = admin.firestore();
    const timestamp = admin.firestore.FieldValue.serverTimestamp();

    const mapaCxC = new Map<string, number>();
    const mapaCxP = new Map<string, number>();
    let movimientosLeidos = 0;
    let movimientosInvalidos = 0;

    const baseQuery = db
      .collectionGroup("movimientos")
      .where("periodo", "==", periodo)
      .orderBy(admin.firestore.FieldPath.documentId())
      .limit(1000);

    let query: admin.firestore.Query = baseQuery;
    for (;;) {
      const snap = await query.get();

      for (const doc of snap.docs) {
        movimientosLeidos++;
        const data = doc.data() as Record<string, unknown>;

        const empresaOrigen = normalizeId(String(data.empresa_origen ?? ""));
        const empresaContraparte = normalizeId(String(data.empresa_contraparte ?? ""));
        const tipo = normalizeTipo(String(data.tipo ?? ""));
        const valor = parseNumber(data.valor);

        if (!empresaOrigen || !empresaContraparte || !tipo || !Number.isFinite(valor)) {
          movimientosInvalidos++;
          continue;
        }

        if (tipo === "CxC") {
          sumInto(mapaCxC, `${empresaOrigen}|${empresaContraparte}`, valor);
        } else {
          sumInto(mapaCxP, `${empresaContraparte}|${empresaOrigen}`, valor);
        }
      }

      if (snap.size < 1000) break;
      query = baseQuery.startAfter(snap.docs[snap.docs.length - 1]!);
    }

    const resultadosRef = db.collection("cruces").doc(periodo).collection("resultados");
    await deleteCollection(resultadosRef);

    await db
      .collection("cruces")
      .doc(periodo)
      .set(
        {
          periodo,
          tolerancia: round2(tolerancia),
          fecha_ejecucion: timestamp,
        },
        { merge: true },
      );

    const claves = new Set<string>([...mapaCxC.keys(), ...mapaCxP.keys()]);
    let paresEvaluados = 0;
    let resultadosGuardados = 0;
    let omitidosAmbosCero = 0;
    let ok = 0;
    let descuadre = 0;

    let pending: Array<{ ref: admin.firestore.DocumentReference; data: CruzarResultado }> = [];
    let opsInBatch = 0;

    const flush = async () => {
      if (pending.length === 0) return;
      await commitSetBatch(db, pending);
      pending = [];
      opsInBatch = 0;
    };

    for (const clave of claves) {
      const [empresaA, empresaB] = clave.split("|");
      if (!empresaA || !empresaB) continue;

      paresEvaluados++;

      const cxC = round2(mapaCxC.get(clave) ?? 0);
      const cxP = round2(mapaCxP.get(clave) ?? 0);
      if (cxC === 0 && cxP === 0) {
        omitidosAmbosCero++;
        continue;
      }

      const diferencia = round2(cxC - cxP);
      const estado: CruzarResultado["estado"] = Math.abs(diferencia) <= tolerancia ? "OK" : "DESCUADRE";
      if (estado === "OK") ok++;
      else descuadre++;

      pending.push({
        ref: resultadosRef.doc(makePairId(empresaA, empresaB)),
        data: {
          empresa_a: empresaA,
          empresa_b: empresaB,
          cxC,
          cxP,
          diferencia,
          estado,
        },
      });

      resultadosGuardados++;
      opsInBatch++;
      if (opsInBatch >= 450) {
        await flush();
      }
    }

    try {
      await flush();
    } catch (err) {
      logger.error("Error finalizando batch", err);
    }

    const response: CruzarResponse = {
      periodo,
      tolerancia: round2(tolerancia),
      movimientos_leidos: movimientosLeidos,
      movimientos_invalidos: movimientosInvalidos,
      pares_evaluados: paresEvaluados,
      resultados_guardados: resultadosGuardados,
      omitidos_ambos_cero: omitidosAmbosCero,
      ok,
      descuadre,
    };

    res.status(200).json(response);
  },
);

async function commitSetBatch(
  db: admin.firestore.Firestore,
  writes: Array<{ ref: admin.firestore.DocumentReference; data: Record<string, unknown> }>,
): Promise<void> {
  const batch = db.batch();
  for (const w of writes) {
    batch.set(w.ref, w.data, { merge: true });
  }
  await batch.commit();
}

async function deleteCollection(col: admin.firestore.CollectionReference): Promise<void> {
  let query: admin.firestore.Query = col.orderBy(admin.firestore.FieldPath.documentId()).limit(450);
  for (;;) {
    const snap = await query.get();
    if (snap.empty) return;

    const batch = col.firestore.batch();
    for (const doc of snap.docs) {
      batch.delete(doc.ref);
    }

    await batch.commit();

    if (snap.size < 450) return;
    query = col
      .orderBy(admin.firestore.FieldPath.documentId())
      .startAfter(snap.docs[snap.docs.length - 1]!)
      .limit(450);
  }
}

function sumInto(map: Map<string, number>, key: string, value: number): void {
  map.set(key, (map.get(key) ?? 0) + value);
}

function makePairId(empresaA: string, empresaB: string): string {
  const a = empresaA.trim().replace(/\s+/g, "_");
  const b = empresaB.trim().replace(/\s+/g, "_");
  return `${a}__${b}`;
}


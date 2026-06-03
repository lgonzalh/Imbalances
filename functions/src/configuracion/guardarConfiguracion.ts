import { onRequest } from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import * as admin from "firebase-admin";

type GuardarConfiguracionRequest = {
  empresas?: any[];
  cuentas?: any[];
  notas?: any[];
  equivalencias?: any[];
};

function generateSlug(text: string): string {
  return (text || "").toString()
    .toLowerCase()
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .replace(/[^a-z0-9]+/g, "_")
    .replace(/(^_|_$)/g, "");
}

async function syncCollection(db: admin.firestore.Firestore, collectionName: string, items: any[], idGenerator: (item: any) => string) {
  const colRef = db.collection(collectionName);
  const snapshot = await colRef.get();
  
  const existingDocs = new Set(snapshot.docs.map(d => d.id));
  const newDocs = new Set<string>();

  const batch = db.batch();

  for (const item of items) {
    const docId = idGenerator(item);
    if (!docId) continue;
    
    newDocs.add(docId);
    batch.set(colRef.doc(docId), { ...item, _updatedAt: admin.firestore.FieldValue.serverTimestamp() }, { merge: true });
  }

  // Delete docs that are no longer in the configuration
  for (const id of existingDocs) {
    if (!newDocs.has(id)) {
      batch.delete(colRef.doc(id));
    }
  }

  await batch.commit();
}

export const guardarConfiguracion = onRequest(
  {
    region: "southamerica-east1",
    cors: true,
  },
  async (req, res) => {
    if (req.method !== "POST") {
      res.status(405).json({ error: "Use POST" });
      return;
    }

    try {
      const body = req.body as GuardarConfiguracionRequest;
      const db = admin.firestore();

      if (body.cuentas && Array.isArray(body.cuentas)) {
        await syncCollection(db, "config_cuentas", body.cuentas, (c) => generateSlug(`${c.tipo}_${c.nombreCuenta}`));
      }

      if (body.empresas && Array.isArray(body.empresas)) {
        await syncCollection(db, "config_empresas", body.empresas, (e) => generateSlug(e.nombreCarpeta));
      }

      if (body.notas && Array.isArray(body.notas)) {
        await syncCollection(db, "config_notas", body.notas, (n) => generateSlug(n.identificador));
      }

      if (body.equivalencias && Array.isArray(body.equivalencias)) {
        await syncCollection(db, "config_equivalencias", body.equivalencias, (eq) => generateSlug(`${eq.tipo}_${eq.cuentaCanonica}`));
      }

      res.status(200).json({ ok: true });
    } catch (error) {
      logger.error("Error saving configuration", error);
      res.status(500).json({ error: "Internal Error" });
    }
  }
);

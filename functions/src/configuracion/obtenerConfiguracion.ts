import { onRequest } from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import * as admin from "firebase-admin";

export const obtenerConfiguracion = onRequest(
  {
    region: "southamerica-east1",
    cors: true,
  },
  async (req, res) => {
    if (req.method !== "GET") {
      res.status(405).json({ error: "Use GET" });
      return;
    }

    try {
      const db = admin.firestore();

      const [cuentasSnap, empresasSnap, notasSnap, equivalenciasSnap] = await Promise.all([
        db.collection("config_cuentas").get(),
        db.collection("config_empresas").get(),
        db.collection("config_notas").get(),
        db.collection("config_equivalencias").get()
      ]);

      const mapDocs = (snap: admin.firestore.QuerySnapshot) => {
        return snap.docs.map(doc => {
          const data = doc.data();
          delete data._updatedAt;
          return data;
        });
      };

      const configuracion = {
        cuentas: mapDocs(cuentasSnap),
        empresas: mapDocs(empresasSnap),
        notas: mapDocs(notasSnap),
        equivalencias: mapDocs(equivalenciasSnap)
      };

      res.status(200).json(configuracion);
    } catch (error) {
      logger.error("Error fetching configuration", error);
      res.status(500).json({ error: "Internal Error" });
    }
  }
);

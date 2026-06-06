import * as functions from "firebase-functions";
import { getFirestore } from "firebase-admin/firestore";

interface LimpiarColeccionRequest {
    coleccion: string;
}

/**
 * Elimina todos los documentos de una colección
 */
export const limpiarColeccion = functions
    .region("southamerica-east1")
    .https.onRequest(async (req, res) => {
        // CORS - Permitir todas las solicitudes
        res.set("Access-Control-Allow-Origin", "*");
        res.set("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        res.set("Access-Control-Allow-Headers", "Content-Type, Authorization");

        // Manejar preflight request
        if (req.method === "OPTIONS") {
            res.status(204).send("");
            return;
        }

        if (req.method !== "POST") {
            res.status(405).json({ error: "Método no permitido" });
            return;
        }

        try {
            const body = req.body as LimpiarColeccionRequest;

            if (!body.coleccion) {
                res.status(400).json({ error: "Falta parámetro 'coleccion'" });
                return;
            }

            const db = getFirestore();
            let eliminados = 0;
            let errores = 0;

            // Eliminar en lotes
            const batchSize = 500;
            let hasMore = true;

            while (hasMore) {
                const snapshot = await db
                    .collection(body.coleccion)
                    .limit(batchSize)
                    .get();

                if (snapshot.empty) {
                    hasMore = false;
                    break;
                }

                const batch = db.batch();
                snapshot.docs.forEach((doc) => {
                    batch.delete(doc.ref);
                });

                try {
                    await batch.commit();
                    eliminados += snapshot.size;
                } catch (error) {
                    console.error("Error al eliminar lote:", error);
                    errores += snapshot.size;
                }

                // Si obtuvimos menos documentos que el límite, no hay más
                if (snapshot.size < batchSize) {
                    hasMore = false;
                }
            }

            res.status(200).json({
                eliminados: eliminados,
                errores: errores,
            });
        } catch (error: unknown) {
            console.error("Error al limpiar colección:", error);
            const errorMessage = error instanceof Error ?
                error.message :
                "Error desconocido";
            res.status(500).json({
                error: "Error al limpiar colección",
                detalle: errorMessage,
            });
        }
    }
    );

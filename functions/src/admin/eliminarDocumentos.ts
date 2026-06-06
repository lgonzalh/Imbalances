import * as functions from "firebase-functions";
import { getFirestore } from "firebase-admin/firestore";

interface EliminarDocumentosRequest {
    coleccion: string;
    documentIds: string[];
}

/**
 * Elimina documentos específicos de una colección
 */
export const eliminarDocumentos = functions
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
            const body = req.body as EliminarDocumentosRequest;

            if (!body.coleccion) {
                res.status(400).json({ error: "Falta parámetro 'coleccion'" });
                return;
            }

            if (!body.documentIds || !Array.isArray(body.documentIds)) {
                res.status(400).json({ error: "Falta parámetro 'documentIds'" });
                return;
            }

            if (body.documentIds.length === 0) {
                res.status(200).json({ eliminados: 0, errores: 0 });
                return;
            }

            const db = getFirestore();
            let eliminados = 0;
            let errores = 0;

            // Eliminar en lotes para mejor rendimiento
            const batch = db.batch();
            const maxBatchSize = 500;

            for (let i = 0; i < body.documentIds.length; i++) {
                const docId = body.documentIds[i];

                try {
                    const docRef = db.collection(body.coleccion).doc(docId);
                    batch.delete(docRef);

                    // Ejecutar el batch si alcanzamos el límite
                    if ((i + 1) % maxBatchSize === 0) {
                        await batch.commit();
                        eliminados += maxBatchSize;
                    }
                } catch (error) {
                    console.error(`Error al eliminar documento ${docId}:`, error);
                    errores++;
                }
            }

            // Ejecutar el batch final si quedan documentos
            const remaining = body.documentIds.length % maxBatchSize;
            if (remaining > 0) {
                await batch.commit();
                eliminados += remaining;
            }

            res.status(200).json({
                eliminados: eliminados,
                errores: errores,
            });
        } catch (error: unknown) {
            console.error("Error al eliminar documentos:", error);
            const errorMessage = error instanceof Error ?
                error.message :
                "Error desconocido";
            res.status(500).json({
                error: "Error al eliminar documentos",
                detalle: errorMessage,
            });
        }
    }
    );

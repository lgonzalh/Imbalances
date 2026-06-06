import * as functions from "firebase-functions";
import { getFirestore } from "firebase-admin/firestore";

/**
 * Lista los documentos de una colección específica
 */
export const listarDocumentos = functions
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

        if (req.method !== "GET") {
            res.status(405).json({ error: "Método no permitido" });
            return;
        }

        try {
            const coleccion = req.query.coleccion as string;
            const limite = parseInt(req.query.limite as string || "100", 10);

            if (!coleccion) {
                res.status(400).json({ error: "Falta parámetro 'coleccion'" });
                return;
            }

            const db = getFirestore();
            const snapshot = await db.collection(coleccion).limit(limite).get();

            const documentos = snapshot.docs.map((doc) => {
                const data = doc.data();
                // Crear una vista previa del documento
                const preview = crearVistaPrevia(data);

                return {
                    id: doc.id,
                    data: data,
                    preview: preview,
                };
            });

            res.status(200).json({
                documentos: documentos,
            });
        } catch (error: unknown) {
            console.error("Error al listar documentos:", error);
            const errorMessage = error instanceof Error ?
                error.message :
                "Error desconocido";
            res.status(500).json({
                error: "Error al listar documentos",
                detalle: errorMessage,
            });
        }
    }
    );

/**
 * Crea una vista previa legible de un documento
 */
function crearVistaPrevia(data: any): string {
    try {
        const keys = Object.keys(data);
        if (keys.length === 0) return "(vacío)";

        const preview: string[] = [];
        for (let i = 0; i < Math.min(3, keys.length); i++) {
            const key = keys[i];
            let value = data[key];

            // Formatear el valor
            if (typeof value === "object" && value !== null) {
                value = Array.isArray(value) ? `[${value.length} items]` : "{...}";
            } else if (typeof value === "string" && value.length > 30) {
                value = value.substring(0, 30) + "...";
            }

            preview.push(`${key}: ${value}`);
        }

        if (keys.length > 3) {
            preview.push(`... (+${keys.length - 3} más)`);
        }

        return preview.join(", ");
    } catch {
        return "(error al generar vista previa)";
    }
}

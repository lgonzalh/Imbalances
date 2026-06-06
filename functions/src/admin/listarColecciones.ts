import * as functions from "firebase-functions";
import { getFirestore } from "firebase-admin/firestore";

/**
 * Lista todas las colecciones disponibles en Firestore
 */
export const listarColecciones = functions
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
            const db = getFirestore();

            // Obtener todas las colecciones
            const collections = await db.listCollections();
            const colecciones = collections.map((col) => col.id);

            res.status(200).json({
                colecciones: colecciones,
            });
        } catch (error: unknown) {
            console.error("Error al listar colecciones:", error);
            const errorMessage = error instanceof Error ?
                error.message :
                "Error desconocido";
            res.status(500).json({
                error: "Error al listar colecciones",
                detalle: errorMessage,
            });
        }
    });

import { onRequest } from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import * as admin from "firebase-admin";
import {
    commitBatch,
    loadExistingHashes,
    normalizeId,
    normalizeTipo,
    parseNumber,
    parseOptionalInt,
    round2,
} from "./utils";

type MovimientoInput = {
    empresa_contraparte: string;
    tipo: string;
    cuenta: string;
    valor: number | string | null;
    nota?: number | string | null;
};

type GuardarMovimientosRequest = {
    empresa: string;
    periodo: string;
    movimientos: MovimientoInput[];
};

type GuardarMovimientosResponse = {
    insertados: number;
    duplicados: number;
    errores: number;
};

export const guardarMovimientos = onRequest(
    {
        region: "southamerica-east1",
        cors: true,
    },
    async (req, res) => {
        if (req.method !== "POST") {
            res.status(405).json({ error: "Use POST" });
            return;
        }

        const body = req.body as GuardarMovimientosRequest;
        const empresa = normalizeId(body?.empresa ?? "");
        const periodo = (body?.periodo ?? "").trim();
        const movimientos = Array.isArray(body?.movimientos)
            ? body.movimientos
            : [];

        if (!empresa) {
            res.status(400).json({ error: "empresa vacía" });
            return;
        }

        if (!/^\d{4}-\d{2}$/.test(periodo)) {
            res.status(400).json({ error: "periodo inválido (YYYY-MM)" });
            return;
        }

        if (movimientos.length === 0) {
            res.status(400).json({ error: "movimientos vacío" });
            return;
        }

        const db = admin.firestore();
        const timestamp = admin.firestore.FieldValue.serverTimestamp();

        const empresaRef = db.collection("empresas").doc(empresa);
        await empresaRef.set(
            {
                id: empresa,
                nombre: empresa,
                fecha_actualizacion: timestamp,
            },
            { merge: true },
        );

        const periodoRef = empresaRef.collection("periodos").doc(periodo);
        await periodoRef.set(
            {
                periodo,
                procesado: true,
                fecha_proceso: timestamp,
            },
            { merge: true },
        );

        const movimientosCol = periodoRef.collection("movimientos");
        const hashesExistentes = await loadExistingHashes(movimientosCol);

        let insertados = 0;
        let duplicados = 0;
        let errores = 0;

        let pending: Array<{
            ref: admin.firestore.DocumentReference;
            data: Record<string, unknown>;
        }> = [];
        let opsInBatch = 0;

        const flush = async () => {
            if (pending.length === 0) return;
            await commitBatch(db, pending, (count) => {
                errores += count;
            });
            pending = [];
            opsInBatch = 0;
        };

        for (const mov of movimientos) {
            const contraparte = normalizeId(mov.empresa_contraparte ?? "");
            const tipo = normalizeTipo(mov.tipo ?? "");
            const cuenta = (mov.cuenta ?? "").trim();
            const valor = parseNumber(mov.valor);
            const nota = parseOptionalInt(mov.nota);

            if (!contraparte || !tipo || !cuenta || !Number.isFinite(valor)) {
                errores++;
                continue;
            }

            const hash = `${empresa}|${contraparte}|${tipo}|${periodo}`;
            if (hashesExistentes.has(hash)) {
                duplicados++;
                continue;
            }

            const ref = movimientosCol.doc(hash);
            const data: Record<string, unknown> = {
                empresa_origen: empresa,
                empresa_contraparte: contraparte,
                tipo,
                cuenta,
                valor: round2(valor),
                nota,
                periodo,
                hash,
                fecha_registro: timestamp,
            };

            pending.push({ ref, data });
            hashesExistentes.add(hash);
            insertados++;
            opsInBatch++;

            if (opsInBatch >= 450) {
                await flush();
            }
        }

        try {
            await flush();
        } catch (err) {
            logger.error("Error finalizando batch", err);
            errores += pending.length;
        }

        const response: GuardarMovimientosResponse = {
            insertados,
            duplicados,
            errores,
        };

        res.status(200).json(response);
    },
);

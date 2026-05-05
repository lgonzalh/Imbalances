import * as admin from "firebase-admin";

if (admin.apps.length === 0) {
  admin.initializeApp();
}

export { guardarMovimientos } from "./motor2/guardarMovimientos";
export { cruzar } from "./motor3";
export { guardarConfiguracion } from "./configuracion/guardarConfiguracion";
export { obtenerConfiguracion } from "./configuracion/obtenerConfiguracion";

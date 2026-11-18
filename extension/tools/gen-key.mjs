// Generates an extension key pair and prints the manifest "key" and the extension Id.
// Run once: node extension/tools/gen-key.mjs. The private key is NOT saved.
import { createHash, generateKeyPairSync } from "node:crypto";
import { fileURLToPath } from "node:url";

/** Extension Id: first 32 hex chars of SHA-256(DER), each digit 0-f mapped to a-p. */
export function extensionIdFromDer(der) {
  const hex = createHash("sha256").update(der).digest("hex").slice(0, 32);
  return [...hex].map((c) => String.fromCharCode("a".charCodeAt(0) + parseInt(c, 16))).join("");
}

function main() {
  const { publicKey } = generateKeyPairSync("rsa", { modulusLength: 2048 });
  const der = publicKey.export({ type: "spki", format: "der" });
  console.log("key: " + der.toString("base64"));
  console.log("Id:  " + extensionIdFromDer(der));
}

if (process.argv[1] === fileURLToPath(import.meta.url)) main();

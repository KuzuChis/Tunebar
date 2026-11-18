import test from "node:test";
import assert from "node:assert/strict";
import { extensionIdFromDer } from "../tools/gen-key.mjs";

test("extension id is sha256 first 32 hex chars mapped 0-f to a-p", () => {
  // sha256("abc") starts with ba7816bf8f01cfea414140de5dae2223
  assert.equal(extensionIdFromDer(Buffer.from("abc")), "lkhibglpipabmpokebebeanofnkocccd");
});

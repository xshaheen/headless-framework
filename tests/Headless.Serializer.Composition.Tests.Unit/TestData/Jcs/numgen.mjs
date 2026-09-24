// Writes "<json literal>,<expected RFC 8785 form>" lines. The expected form is ECMAScript's own Number#toString,
// which RFC 8785 adopts; the literal is toExponential(16), a different spelling of the same double.
import { writeFileSync } from "node:fs";

const [, , outPath, countArg] = process.argv;
const count = Number(countArg);
const view = new DataView(new ArrayBuffer(8));

const fromBits = (hex) => {
  view.setBigUint64(0, BigInt("0x" + hex));
  return view.getFloat64(0);
};

const edges = [
  // Samples published with the RFC 8785 reference number test file.
  "4340000000000001", "4340000000000002", "444b1ae4d6e2ef50", "3eb0c6f7a0b5ed8d", "3eb0c6f7a0b5ed8c",
  "8000000000000000", "0000000000000000", "0000000000000001", "8000000000000001", "7fefffffffffffff",
  "ffefffffffffffff", "4332000000000000", "c332000000000000", "3ff0000000000000", "bff0000000000000",
].map(fromBits);

edges.push(
  1e21, 1e21 - 65536, 999999999999999900000, 1e-6, 1e-7, 9.999999999999997e-7, 0.1, 0.2 + 0.1, 1 / 3,
  333333333.3333333, 1e30, 4.5, 0.002, 1e-27, 295147905179352830000, 2 ** 53, 2 ** 53 - 1, -(2 ** 53),
  5e-324, Number.MAX_VALUE, Number.EPSILON, 123456789012345680000, 1.5, -1.5, 100, 1e20, 1e22,
);

// xorshift64* keeps the sample deterministic, so the committed file is reproducible.
let state = 0x9e3779b97f4a7c15n;
const next = () => {
  state ^= state >> 12n;
  state ^= (state << 25n) & 0xffffffffffffffffn;
  state ^= state >> 27n;
  return (state * 0x2545f4914f6cdd1dn) & 0xffffffffffffffffn;
};

const values = [...edges];

while (values.length < count) {
  view.setBigUint64(0, next());
  const value = view.getFloat64(0);

  if (Number.isFinite(value)) {
    values.push(value);
  }
}

const lines = values.map((value) => `${value.toExponential(16)},${Object.is(value, -0) ? "0" : String(value)}`);
writeFileSync(outPath, lines.join("\n") + "\n");

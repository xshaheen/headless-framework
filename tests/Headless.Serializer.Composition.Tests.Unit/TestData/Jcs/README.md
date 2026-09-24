# RFC 8785 test vectors

`input/` and `output/` are copied byte for byte from the RFC 8785 reference implementation's `testdata`
directory (https://github.com/cyberphone/json-canonicalization, Apache License 2.0). Each `input` file
canonicalizes to the `output` file of the same name.

`numbers.csv` holds `<JSON literal>,<canonical form>` lines. RFC 8785 serializes numbers exactly as ECMAScript's
`Number.prototype.toString`, so JavaScript itself is the oracle: `numgen.mjs` writes each double as
`toExponential(16)` (a different spelling of the same value) next to `String(value)`. The first lines are the samples
published with the reference implementation's number test file; the rest are finite doubles from a seeded
generator, so the file is reproducible:

```bash
node numgen.mjs numbers.csv 5000
```

// Measures how many live-query runs a batch of writes causes. It opens one stream per owner, adds todos spread
// across the owners, and reads the server's meter totals before and after.
//
// Usage: node tools/bench-invalidation.mjs <base-url> [owners=40] [writes=80]     (Node 22 or later)
//
// Run it against a host started with LiveQueries__RowLevelMatching=true and again with =false: the same updates
// are delivered, and the number of runs differs.

const [base, ownersText = "40", writesText = "80"] = process.argv.slice(2);

if (!base) {
  console.error("Usage: node tools/bench-invalidation.mjs <base-url> [owners=40] [writes=80]");
  process.exit(2);
}

const owners = Number(ownersText);
const writes = Number(writesText);
const prefix = "headless.live_queries.";
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));

async function meter() {
  const { rowLevelMatching, totals } = await (await fetch(`${base}/demo/meter`)).json();
  const read = name => totals[prefix + name] ?? 0;

  return { rowLevelMatching, runs: read("runs"), wakes: read("wakes"), ruledOut: read("rows.ruled_out") };
}

/** Waits until the meter stops moving, so runs still in flight are counted. */
async function settled() {
  for (let last = await meter(), quiet = 0; ; ) {
    await pause(150);
    const next = await meter();
    quiet = next.runs === last.runs && next.wakes === last.wakes ? quiet + 1 : 0;

    if (quiet === 3) {
      return next;
    }

    last = next;
  }
}

// One NDJSON stream per owner. Each keeps its result lines and tells a waiter when the next one arrives.
const abort = new AbortController();

async function open(owner) {
  const stream = { owner, results: [], waiters: [] };
  const response = await fetch(`${base}/todos/owner/${owner}`, {
    headers: { Accept: "application/x-ndjson" },
    signal: abort.signal,
  });

  if (!response.ok) {
    throw new Error(`The stream of owner ${owner} answered ${response.status}.`);
  }

  (async () => {
    let buffer = "";

    try {
      for await (const chunk of response.body.pipeThrough(new TextDecoderStream())) {
        buffer += chunk;

        for (let end = buffer.indexOf("\n"); end >= 0; end = buffer.indexOf("\n")) {
          const line = JSON.parse(buffer.slice(0, end));
          buffer = buffer.slice(end + 1);

          if (line.kind === "result") {
            stream.results.push(line.value);
            stream.waiters.splice(0).forEach(resolve => resolve());
          }
        }
      }
    } catch {
      // Aborted when the benchmark ends.
    }
  })();

  await next(stream);

  return stream;
}

function next(stream) {
  return new Promise(resolve => stream.waiters.push(resolve));
}

const streams = await Promise.all(Array.from({ length: owners }, (_, index) => open(index + 1)));
const initial = streams.map(stream => stream.results[0].length);
const before = await settled();
const started = performance.now();

for (let write = 0; write < writes; write++) {
  const stream = streams[write % owners];
  const delivered = next(stream);

  const response = await fetch(`${base}/todos`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ title: `bench ${write + 1}`, ownerId: stream.owner }),
  });

  if (!response.ok) {
    throw new Error(`Write ${write + 1} answered ${response.status}.`);
  }

  // The next write waits for this one's update, so every stream is idle or already signalled when it lands.
  await delivered;
}

const elapsed = Math.round(performance.now() - started);
const after = await settled();
abort.abort();

// Every owner's last result must hold exactly the todos written for that owner on top of what it started with.
const wrong = streams.filter((stream, index) => {
  const expected = initial[index] + Array.from({ length: writes }, (_, write) => write % owners).filter(o => o === index).length;

  return stream.results.at(-1).length !== expected;
}).length;
const changed = streams.reduce((sum, stream) => sum + stream.results.length - 1, 0);

console.log(`rowLevelMatching=${after.rowLevelMatching} streams=${owners} writes=${writes} elapsed=${elapsed}ms`);
console.log(`  query runs caused by the writes   : ${after.runs - before.runs}`);
console.log(`  wakes                             : ${after.wakes - before.wakes}`);
console.log(`  rows ruled out by an owner filter : ${after.ruledOut - before.ruledOut}`);
console.log(`  changed results delivered         : ${changed}`);
console.log(`  streams with a wrong final list   : ${wrong}`);
console.log("  (runs and wakes include the host's own stats watcher, which every write wakes)");
process.exit(wrong === 0 && changed === writes ? 0 : 1);

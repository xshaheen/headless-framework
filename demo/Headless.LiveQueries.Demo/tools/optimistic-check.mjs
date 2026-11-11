// Drives the demo's browser client (wwwroot/headless-live.js) against the running demo from Node, prints what a
// subscriber would show over time, and checks the optimistic timeline: an expected result shows at once, is never
// shown twice or dropped early, and leaves exactly when the server's own result holds the command.
//
// Usage: node tools/optimistic-check.mjs <base-url>     (Node 22 or later)

const base = process.argv[2];

if (!base) {
  console.error("Usage: node tools/optimistic-check.mjs <base-url>");
  process.exit(2);
}

const { connect } = await import("../wwwroot/headless-live.js");
const client = await connect(base);
const { api } = client;
const started = Date.now();
const at = () => `${String(Date.now() - started).padStart(5)} ms`;
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
let failures = 0;

function check(condition, text) {
  console.log(`${at()}  ${condition ? "PASS" : "FAIL"}  ${text}`);
  failures += condition ? 0 : 1;
}

/** Waits until `predicate()` holds, checked on every change of `query`, for at most `timeoutMs`. */
function until(query, predicate, timeoutMs = 5000) {
  return new Promise(resolve => {
    // subscribe calls back at once when the query already has a value, before it has returned the unsubscribe.
    let stop = () => {};
    let done = false;
    const timer = setTimeout(() => finish(false), timeoutMs);

    function finish(result) {
      done = true;
      clearTimeout(timer);
      stop();
      resolve(result);
    }

    stop = query.subscribe(() => !done && predicate() && finish(true));

    if (done) {
      stop();
    }
  });
}

// Two streams of this client, and one of a second client with its own session id.
const stats = client.live(api.getTodoStats);
const counter = client.live(api.getCounter, {}, { format: "text/event-stream" });
const other = await connect(base);
const otherStats = other.live(api.getTodoStats);
const otherEvents = [];
otherStats.onEvent(event => otherEvents.push(event));

const shown = [];
stats.subscribe((value, query) => {
  shown.push(value.total);
  console.log(
    `${at()}  shown ${value.done}/${value.total}   server ${query.serverValue.done}/${query.serverValue.total}   pending patches ${query.pendingCount}`
  );
});

const statsEvents = [];
stats.onEvent(event => statsEvents.push(event));
const counterEvents = [];
counter.onEvent(event => counterEvents.push(event));

await Promise.all([
  until(stats, () => stats.serverValue !== undefined),
  until(counter, () => true),
  until(otherStats, () => true),
]);
const total = stats.serverValue.total;
const plusOne = value => ({ ...value, total: value.total + 1 });

console.log(`${at()}  -> AddTodo behind 800 ms of latency, with total+1 shown at once on the stats query`);
shown.length = 0;
const sending = client.send(
  api.addTodo,
  { title: "optimistic check", ownerId: 3 },
  // The counter patch changes nothing: it stands for a patch on a query the command turns out not to touch.
  { delayMs: 800, optimistic: [[stats, plusOne], [counter, value => value]] }
);
check(stats.value.total === total + 1 && stats.pendingCount === 1, "the expected result shows before the command is sent");

const add = await sending;
console.log(`${at()}  <- AddTodo answered, acknowledgement: ${add.acknowledgement}`);
check(add.acknowledgement === "pending", "a command that committed a tracked write answers 'pending'");

await until(stats, () => stats.pendingCount === 0);
await until(counter, () => counter.pendingCount === 0);
check(stats.serverValue.total === total + 1 && stats.value.total === total + 1, "the server's result replaced the expected one");
check(shown.every(value => value === total + 1), `every value shown in between was ${total + 1}: no flash back, no double count`);
check(
  statsEvents.some(event => event.kind === "result" && event.acknowledged.includes(add.command)),
  "the stats stream reported the command id with the result that holds it"
);
check(
  counterEvents.some(event => event.kind === "acknowledged" && event.acknowledged.includes(add.command)),
  "the counter stream, which the write does not touch, reported the id alone and dropped its patch"
);
await until(otherStats, () => otherStats.serverValue.total === total + 1);
check(
  otherStats.serverValue.total === total + 1 && otherEvents.every(event => event.acknowledged.length === 0),
  "the other client received the new result and no command id"
);

console.log(`${at()}  -> AddTodo with an empty title (the server rejects it), with total+1 shown at once`);
const rejected = await client
  .send(api.addTodo, { title: "" }, { delayMs: 300, optimistic: [[stats, plusOne]] })
  .then(() => undefined, error => error);
console.log(`${at()}  <- rejected with ${rejected?.status}`);
check(rejected?.status === 422 || rejected?.status === 400, "the server rejected the command");
check(stats.value.total === total + 1 && stats.pendingCount === 0, "the expected result was rolled back");

console.log(`${at()}  -> SummarizeTodos (commits nothing), with total+1 shown at once`);
const summary = await client.send(api.summarizeTodos, {}, { optimistic: [[stats, plusOne]] });
console.log(`${at()}  <- answered, acknowledgement: ${summary.acknowledgement}`);
check(summary.acknowledgement === "none", "a command that committed nothing answers 'none'");
check(stats.value.total === total + 1 && stats.pendingCount === 0, "its patch left with the response, with no stream event to wait for");

await pause(100);
[stats, counter, otherStats].forEach(query => query.close());
console.log(failures === 0 ? "optimistic-check: all checks passed" : `optimistic-check: ${failures} check(s) failed`);
process.exit(failures === 0 ? 0 : 1);

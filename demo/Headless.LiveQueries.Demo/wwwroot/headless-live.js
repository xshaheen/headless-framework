// Demo code, not a published package: a small browser client for a Headless API with live queries.
//
// It states no route. It reads the server's OpenAPI document and exposes one descriptor per operation, keyed by
// the operation id in camelCase. An operation that carries the x-headless-live extension can be opened as a stream.
//
//   const client = await connect();
//   const todos = client.live(client.api.listOwnerTodos, { ownerId: 1 });
//   todos.subscribe(list => render(list));
//   await client.send(client.api.addTodo, { title }, { optimistic: [[todos, list => [...list, draft]]] });

const SESSION_HEADER = "X-Headless-Live-Session";
const COMMAND_HEADER = "X-Headless-Live-Command";
const ACKNOWLEDGEMENT_HEADER = "X-Headless-Live-Acknowledgement";
const LIVE_EXTENSION = "x-headless-live";
const NDJSON = "application/x-ndjson";
const EVENT_STREAM = "text/event-stream";
const METHODS = ["get", "post", "put", "patch", "delete"];

// What a reconnect waits when the server named no delay. The server-sent events format names one; NDJSON does not.
const DEFAULT_RECONNECT_MS = 3000;

export async function connect(base = "", documentPath = "/openapi/v1.json") {
  const response = await fetch(base + documentPath, { headers: { Accept: "application/json" } });

  if (!response.ok) {
    throw new Error(`The OpenAPI document answered ${response.status}.`);
  }

  return new Client(base, readOperations(await response.json()));
}

/** Turns an OpenAPI document into `{ operationId: { name, method, path, parameters, hasBody, live, mediaTypes } }`. */
export function readOperations(document) {
  const api = {};

  for (const [path, item] of Object.entries(document.paths ?? {})) {
    for (const method of METHODS) {
      const operation = item[method];

      if (!operation?.operationId) {
        continue;
      }

      const live = operation[LIVE_EXTENSION];

      api[operation.operationId[0].toLowerCase() + operation.operationId.slice(1)] = {
        name: operation.operationId,
        method: method.toUpperCase(),
        path,
        parameters: [...(item.parameters ?? []), ...(operation.parameters ?? [])].map(({ name, in: location }) => ({
          name,
          in: location,
        })),
        hasBody: operation.requestBody !== undefined,
        // The extension's presence is the marker; mediaTypes are the Accept values that open a stream.
        live: live !== undefined,
        mediaTypes: live?.mediaTypes ?? [],
      };
    }
  }

  return api;
}

export class ApiError extends Error {
  constructor(status, problem) {
    super(problem?.detail ?? problem?.title ?? `The server answered ${status}.`);
    this.status = status;
    this.problem = problem;
  }
}

class Client {
  constructor(base, api) {
    this.base = base;
    this.api = api;

    // One id for this client, sent on every stream and every command. The server reports a command only on
    // streams opened with the same id by the same user, so another tab, with its own id, is told nothing.
    this.session = crypto.randomUUID();
  }

  /**
   * Sends an operation once and returns `{ value, command, acknowledgement }`.
   * `optimistic` is a list of `[liveQuery, patch]` pairs: each patch shows the expected result at once.
   */
  async send(operation, args = {}, { optimistic = [], delayMs = 0 } = {}) {
    // Without an optimistic result nothing waits for an acknowledgement, so the command is not tagged.
    const command = optimistic.length > 0 ? crypto.randomUUID() : undefined;
    const patches = optimistic.map(([query, patch]) => query.patch(command, patch));

    try {
      if (delayMs > 0) {
        await new Promise(resolve => setTimeout(resolve, delayMs));
      }

      const response = await fetch(this.base + buildUrl(operation, args), {
        method: operation.method,
        headers: {
          Accept: "application/json",
          [SESSION_HEADER]: this.session,
          ...(operation.hasBody ? { "Content-Type": "application/json" } : {}),
          ...(command ? { [COMMAND_HEADER]: command } : {}),
        },
        body: operation.hasBody ? JSON.stringify(args) : undefined,
      });

      if (!response.ok) {
        throw new ApiError(response.status, await response.json().catch(() => null));
      }

      // "pending": the command committed a tracked write and each of this client's streams will report its id.
      // "none", or no header: no stream will, so the patches have nothing to wait for.
      const acknowledgement = response.headers.get(ACKNOWLEDGEMENT_HEADER) ?? "none";
      patches.forEach(patch => (acknowledgement === "pending" ? patch.responded() : patch.remove()));

      const value = response.status === 204 ? undefined : await response.json();

      return { value, command, acknowledgement };
    } catch (error) {
      patches.forEach(patch => patch.remove());

      throw error;
    }
  }

  /** Opens a live operation as a stream. `format` is one of the operation's media types; NDJSON by default. */
  live(operation, args = {}, { format } = {}) {
    if (!operation.live) {
      throw new Error(`${operation.name} is not a live operation.`);
    }

    const accept = format ?? (operation.mediaTypes.includes(NDJSON) ? NDJSON : operation.mediaTypes[0]);

    return new LiveQuery(this.base + buildUrl(operation, args), operation.name, accept, this.session);
  }
}

class LiveQuery {
  #url;
  #accept;
  #session;
  #abort = new AbortController();
  #listeners = new Set();
  #eventListeners = new Set();
  #patches = [];
  #firstResultIn = false;
  #reconnectMs = DEFAULT_RECONNECT_MS;

  constructor(url, name, accept, session) {
    this.name = name;
    this.serverValue = undefined;
    this.#url = url;
    this.#accept = accept;
    this.#session = session;
    this.#run();
  }

  /** The value to show: the server's last result with every pending optimistic patch applied on top. */
  get value() {
    return this.serverValue === undefined
      ? undefined
      : this.#patches.reduce((value, patch) => patch.apply(value), this.serverValue);
  }

  get pendingCount() {
    return this.#patches.length;
  }

  subscribe(listener) {
    this.#listeners.add(listener);

    if (this.serverValue !== undefined) {
      listener(this.value, this);
    }

    return () => this.#listeners.delete(listener);
  }

  /** Reports what the stream delivers, for logging: `{ query, kind, acknowledged, value }`. */
  onEvent(listener) {
    this.#eventListeners.add(listener);

    return () => this.#eventListeners.delete(listener);
  }

  /** Shows `apply(serverValue)` until the command is known to be in the server's result, or known to have failed. */
  patch(command, apply) {
    // A stream's first result already holds every command committed before the stream opened, and no id will
    // come for those. A patch made before the first result is therefore dropped by that result, not by an id.
    const patch = { command, apply, responded: false, awaitsFirstResult: !this.#firstResultIn, firstResultIn: false };
    this.#patches.push(patch);
    this.#notify();

    return {
      remove: () => this.#remove(candidate => candidate === patch),
      responded: () => {
        patch.responded = true;

        if (patch.firstResultIn) {
          this.#remove(candidate => candidate === patch);
        }
      },
    };
  }

  close() {
    this.#abort.abort();
  }

  async #run() {
    while (!this.#abort.signal.aborted) {
      // The ids reported to the stream this one replaces are gone, so every patch now waits for the first result.
      this.#firstResultIn = false;
      this.#patches.forEach(patch => (patch.awaitsFirstResult = true));

      try {
        const response = await fetch(this.#url, {
          headers: { Accept: this.#accept, [SESSION_HEADER]: this.#session },
          signal: this.#abort.signal,
        });

        if (!response.ok) {
          // The first run did not succeed, so the server sent its ordinary response and opened no stream.
          this.#fail(await response.json().catch(() => ({ status: response.status })));

          return;
        }

        const read = this.#accept === NDJSON ? readNdjson : readServerSentEvents;

        for await (const update of read(response.body)) {
          if (update.retry !== undefined) {
            this.#reconnectMs = update.retry;
          } else if (update.kind === "failure") {
            // A failure is the stream's last update, and the server ends the response after it.
            this.#fail(update.value);

            return;
          } else if (update.kind === "result") {
            this.#result(update.value, update.acknowledged);
          } else if (update.kind === "acknowledged") {
            this.#acknowledge(update.acknowledged);
          }
        }
      } catch {
        // Closed by close(), or the connection dropped. The loop decides which.
      }

      if (!this.#abort.signal.aborted) {
        this.#notifyEvent("reconnecting", [], undefined);
        await new Promise(resolve => setTimeout(resolve, this.#reconnectMs));
      }
    }
  }

  #result(value, acknowledged) {
    this.serverValue = value;
    const commands = new Set(acknowledged);
    const first = !this.#firstResultIn;
    this.#firstResultIn = true;

    // One step: the new server result goes in and the patches it holds come off, with no render in between.
    this.#patches = this.#patches.filter(patch => {
      if (commands.has(patch.command)) {
        return false;
      }

      if (first && patch.awaitsFirstResult) {
        patch.firstResultIn = true;

        // Until the command's own response arrives it may still fail, so the patch stays for that long.
        return !patch.responded;
      }

      return true;
    });

    this.#notifyEvent("result", acknowledged, value);
    this.#notify();
  }

  #acknowledge(acknowledged) {
    // The last result already reflects these commands: keep it and drop their patches.
    const commands = new Set(acknowledged);
    this.#patches = this.#patches.filter(patch => !commands.has(patch.command));
    this.#notifyEvent("acknowledged", acknowledged, this.serverValue);
    this.#notify();
  }

  #fail(problem) {
    this.#abort.abort();
    this.#patches = [];
    this.#notifyEvent("failure", [], problem);
    this.#notify();
  }

  #remove(predicate) {
    const before = this.#patches.length;
    this.#patches = this.#patches.filter(patch => !predicate(patch));

    if (this.#patches.length !== before) {
      this.#notify();
    }
  }

  #notify() {
    if (this.serverValue !== undefined) {
      this.#listeners.forEach(listener => listener(this.value, this));
    }
  }

  #notifyEvent(kind, acknowledged, value) {
    this.#eventListeners.forEach(listener => listener({ query: this.name, kind, acknowledged, value }));
  }
}

// One JSON object per line: {"kind":"result","version":"…","acknowledged":["…"],"value":…}. A kind this client
// does not know, such as "heartbeat", falls through the caller's checks.
async function* readNdjson(body) {
  for await (const line of readBlocks(body, "\n")) {
    if (line.trim() !== "") {
      const { kind, value, acknowledged = [] } = JSON.parse(line);

      yield { kind, value, acknowledged };
    }
  }
}

// Events separated by a blank line. A result's id is "<version>" or "<version>;<command id>,<command id>". The
// version token is opaque and plays no part here: a client never compares versions to place a command.
async function* readServerSentEvents(body) {
  for await (const block of readBlocks(body, "\n\n")) {
    const fields = {};

    for (const line of block.split("\n")) {
      const colon = line.indexOf(":");

      // A line that starts with a colon is a comment; the server's heartbeat is one.
      if (colon > 0) {
        fields[line.slice(0, colon)] = line.slice(colon + 1).trimStart();
      }
    }

    if (fields.retry !== undefined) {
      yield { retry: Number(fields.retry) };
    }

    if (fields.event === "result") {
      const commands = (fields.id ?? "").split(";")[1];

      yield { kind: "result", value: JSON.parse(fields.data), acknowledged: commands ? commands.split(",") : [] };
    } else if (fields.event === "acknowledged") {
      yield { kind: "acknowledged", acknowledged: JSON.parse(fields.data) };
    } else if (fields.event === "failure") {
      yield { kind: "failure", value: JSON.parse(fields.data) };
    }
  }
}

async function* readBlocks(body, separator) {
  const reader = body.pipeThrough(new TextDecoderStream()).getReader();
  let buffer = "";

  for (;;) {
    const { value, done } = await reader.read();

    if (done) {
      return;
    }

    buffer += value;

    for (let end = buffer.indexOf(separator); end >= 0; end = buffer.indexOf(separator)) {
      yield buffer.slice(0, end);
      buffer = buffer.slice(end + separator.length);
    }
  }
}

function buildUrl(operation, args) {
  const path = operation.path.replace(/\{(\w+)\}/g, (match, name) => {
    const key = findKey(args, name);

    return key === undefined ? match : encodeURIComponent(args[key]);
  });

  const query = new URLSearchParams();

  for (const parameter of operation.parameters.filter(candidate => candidate.in === "query")) {
    const key = findKey(args, parameter.name);

    if (key !== undefined && args[key] !== undefined && args[key] !== null) {
      query.set(parameter.name, String(args[key]));
    }
  }

  const text = query.toString();

  return text ? `${path}?${text}` : path;
}

function findKey(args, name) {
  return Object.keys(args).find(candidate => candidate.toLowerCase() === name.toLowerCase());
}

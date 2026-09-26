package apnsoracle;

import com.eatthepath.json.JsonParser;
import com.eatthepath.json.JsonSerializer;
import com.eatthepath.pushy.apns.ApnsClient;
import com.eatthepath.pushy.apns.ApnsClientBuilder;
import com.eatthepath.pushy.apns.DeliveryPriority;
import com.eatthepath.pushy.apns.PushNotificationResponse;
import com.eatthepath.pushy.apns.PushType;
import com.eatthepath.pushy.apns.auth.ApnsSigningKey;
import com.eatthepath.pushy.apns.auth.AuthenticationToken;
import com.eatthepath.pushy.apns.server.AcceptAllPushNotificationHandlerFactory;
import com.eatthepath.pushy.apns.server.MockApnsServer;
import com.eatthepath.pushy.apns.server.MockApnsServerBuilder;
import com.eatthepath.pushy.apns.server.MockApnsServerListener;
import com.eatthepath.pushy.apns.server.RejectionReason;
import com.eatthepath.pushy.apns.util.ApnsPayloadBuilder;
import com.eatthepath.pushy.apns.util.InterruptionLevel;
import com.eatthepath.pushy.apns.util.LiveActivityEvent;
import com.eatthepath.pushy.apns.util.SimpleApnsPayloadBuilder;
import com.eatthepath.pushy.apns.util.SimpleApnsPushNotification;
import io.netty.buffer.ByteBuf;
import io.netty.handler.codec.http2.Http2Headers;

import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Instant;
import java.util.ArrayList;
import java.util.Base64;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Properties;
import java.util.TreeMap;
import java.util.UUID;
import java.util.concurrent.LinkedBlockingQueue;
import java.util.concurrent.TimeUnit;

/**
 * Oracle generator for pushy. Sends every scenario through a real {@link ApnsClient} to pushy's own
 * {@link MockApnsServer} and records the request as it arrived on the wire, so the fixture reflects pushy's header
 * and payload building rather than a transcription of it.
 */
public final class Generate {

    private static final String[] HEADER_NAMES = {
        "apns-push-type", "apns-topic", "apns-priority", "apns-expiration", "apns-collapse-id", "apns-id",
    };

    /** Marker for pushy's wall-clock default expiration, which cannot be fixed without bypassing that default. */
    private static final String DEFAULT_EXPIRATION_MARKER =
        "<now+" + SimpleApnsPushNotification.DEFAULT_EXPIRATION_PERIOD.getSeconds() + ">";

    private record Capture(Http2Headers headers, String body) {}

    private static final class Unsupported extends Exception {
        Unsupported(final String message) {
            super(message);
        }
    }

    public static void main(final String[] args) throws Exception {
        final Path oraclesDir = Path.of(args.length > 0 ? args[0] : "..").toAbsolutePath().normalize();
        final Path repoRoot = oraclesDir.getParent().getParent();
        final Path outPath = repoRoot.resolve(
            "tests/Headless.PushNotifications.Apns.Tests.Unit/CrossLibrary/Fixtures/pushy.json");

        final Map<String, Object> spec = new JsonParser().parseJsonObject(
            Files.readString(oraclesDir.resolve("scenarios.json"), StandardCharsets.UTF_8));
        @SuppressWarnings("unchecked")
        final Map<String, Object> jwtSpec = (Map<String, Object>) spec.get("jwt");
        final String deviceToken = (String) spec.get("deviceToken");

        final File cert = oraclesDir.resolve("test-keys/localhost.TEST-ONLY.crt").toFile();
        final File key = oraclesDir.resolve("test-keys/localhost.TEST-ONLY.key").toFile();

        final LinkedBlockingQueue<Capture> captures = new LinkedBlockingQueue<>();
        final MockApnsServer server = new MockApnsServerBuilder()
            .setServerCredentials(cert, key, null)
            .setHandlerFactory(new AcceptAllPushNotificationHandlerFactory())
            .setListener(new MockApnsServerListener() {
                @Override
                public void handlePushNotificationAccepted(final Http2Headers headers, final ByteBuf payload) {
                    captures.add(new Capture(headers, payload == null ? "" : payload.toString(StandardCharsets.UTF_8)));
                }

                @Override
                public void handlePushNotificationRejected(final Http2Headers headers, final ByteBuf payload,
                                                           final RejectionReason reason, final Instant expiry) {
                    throw new IllegalStateException("mock server rejected a notification: " + reason);
                }
            })
            .build();
        final int port = server.start(0).get(30, TimeUnit.SECONDS);

        final ApnsSigningKey signingKey = ApnsSigningKey.loadFromPkcs8File(
            oraclesDir.resolve((String) jwtSpec.get("keyFile")).toFile(),
            (String) jwtSpec.get("teamId"),
            (String) jwtSpec.get("keyId"));

        final ApnsClient client = new ApnsClientBuilder()
            .setApnsServer("localhost", port)
            .setTrustedServerCertificateChain(cert)
            .setSigningKey(signingKey)
            .build();

        final List<Object> scenarios = new ArrayList<>();
        String wireAuthorization = null;

        try {
            @SuppressWarnings("unchecked")
            final List<Map<String, Object>> scenarioSpecs = (List<Map<String, Object>>) spec.get("scenarios");
            for (final Map<String, Object> scenario : scenarioSpecs) {
                final String id = (String) scenario.get("id");
                final SimpleApnsPushNotification notification;
                final boolean defaultExpiration = !scenario.containsKey("expiration");
                try {
                    notification = buildNotification(scenario, deviceToken);
                } catch (final Unsupported e) {
                    final Map<String, Object> entry = new LinkedHashMap<>();
                    entry.put("id", id);
                    entry.put("supported", false);
                    entry.put("unsupportedReason", e.getMessage());
                    entry.put("headers", null);
                    entry.put("payload", null);
                    scenarios.add(entry);
                    continue;
                }

                final long sentAt = Instant.now().getEpochSecond();
                final PushNotificationResponse<SimpleApnsPushNotification> response =
                    client.sendNotification(notification).get(30, TimeUnit.SECONDS);
                if (!response.isAccepted()) {
                    throw new IllegalStateException("scenario " + id + " was not accepted: " + response);
                }
                final Capture capture = captures.poll(30, TimeUnit.SECONDS);
                if (capture == null) {
                    throw new IllegalStateException("scenario " + id + " was not captured");
                }
                final CharSequence requestPath = capture.headers().path();
                if (requestPath == null || !("/3/device/" + deviceToken).contentEquals(requestPath)) {
                    throw new IllegalStateException("scenario " + id + " used path " + requestPath);
                }

                final Map<String, Object> headers = new LinkedHashMap<>();
                for (final String name : HEADER_NAMES) {
                    final CharSequence value = capture.headers().get(name);
                    headers.put(name, value == null ? null : value.toString());
                }
                if (defaultExpiration) {
                    final long expiration = Long.parseLong((String) headers.get("apns-expiration"));
                    final long expected = sentAt + SimpleApnsPushNotification.DEFAULT_EXPIRATION_PERIOD.getSeconds();
                    if (Math.abs(expiration - expected) > 5) {
                        throw new IllegalStateException("scenario " + id + " default expiration " + expiration
                            + " is not " + expected);
                    }
                    headers.put("apns-expiration", DEFAULT_EXPIRATION_MARKER);
                }

                final Map<String, Object> entry = new LinkedHashMap<>();
                entry.put("id", id);
                entry.put("supported", true);
                entry.put("unsupportedReason", null);
                entry.put("headers", headers);
                entry.put("payload", capture.body().isEmpty() ? null : sorted(new JsonParser().parseJsonObject(capture.body())));
                scenarios.add(entry);

                if (wireAuthorization == null) {
                    wireAuthorization = capture.headers().get("authorization").toString();
                }
            }
        } finally {
            client.close().get(30, TimeUnit.SECONDS);
            server.shutdown().get(30, TimeUnit.SECONDS);
        }

        // pushy's AuthenticationToken takes the issue time as a constructor argument, so the fixture pins iat to the
        // scenario clock. The token the client actually sent must have the same header and issuer.
        final Instant issuedAt = Instant.ofEpochSecond(((Number) jwtSpec.get("issuedAt")).longValue());
        final Map<String, Object> jwt = decodeJwt(
            new AuthenticationToken(signingKey, issuedAt).getAuthorizationHeader().toString());
        final Map<String, Object> wireJwt = decodeJwt(wireAuthorization);
        if (!wireJwt.get("header").equals(jwt.get("header"))
            || !((Map<?, ?>) wireJwt.get("claims")).keySet().equals(((Map<?, ?>) jwt.get("claims")).keySet())
            || !((Map<?, ?>) wireJwt.get("claims")).get("iss").equals(((Map<?, ?>) jwt.get("claims")).get("iss"))) {
            throw new IllegalStateException("wire token " + wireJwt + " differs in shape from " + jwt);
        }

        final Map<String, Object> output = new LinkedHashMap<>();
        output.put("library", "com.eatthepath:pushy");
        output.put("version", pushyVersion());
        output.put("generatedAt", spec.get("generatedAt"));
        output.put("scenarios", scenarios);
        output.put("jwt", sorted(jwt));

        final StringBuilder json = new StringBuilder();
        writeJson(json, output, 0);
        json.append('\n');
        Files.createDirectories(outPath.getParent());
        Files.writeString(outPath, json.toString(), StandardCharsets.UTF_8);
        System.out.println("pushy: wrote " + scenarios.size() + " scenarios to " + repoRoot.relativize(outPath));
        System.exit(0);
    }

    /** Reads the version from the resolved pushy jar itself, so the fixture cannot drift from pom.xml. */
    private static String pushyVersion() throws Exception {
        try (var stream = ApnsClient.class.getResourceAsStream("/META-INF/maven/com.eatthepath/pushy/pom.properties")) {
            final Properties properties = new Properties();
            properties.load(stream);
            return properties.getProperty("version");
        }
    }

    @SuppressWarnings("unchecked")
    private static SimpleApnsPushNotification buildNotification(final Map<String, Object> scenario,
                                                                final String deviceToken) throws Unsupported {
        final String pushTypeValue = (String) scenario.get("pushType");
        PushType pushType = null;
        for (final PushType candidate : PushType.values()) {
            if (candidate.getHeaderValue().equals(pushTypeValue)) {
                pushType = candidate;
            }
        }
        if (pushType == null) {
            throw new Unsupported("PushType enum has no value for apns-push-type '" + pushTypeValue + "'");
        }

        final String payload;
        if (scenario.containsKey("raw")) {
            // pushy's notification payload is a caller-supplied JSON string, so a raw payload is native to it.
            payload = JsonSerializer.writeJsonTextAsString((Map<String, Object>) scenario.get("raw"));
        } else {
            final ApnsPayloadBuilder builder = new SimpleApnsPayloadBuilder();
            final Map<String, Object> aps = (Map<String, Object>) scenario.getOrDefault("aps", Map.of());
            for (final Map.Entry<String, Object> entry : aps.entrySet()) {
                applyAps(builder, entry.getKey(), entry.getValue());
            }
            final Map<String, Object> custom = (Map<String, Object>) scenario.getOrDefault("custom", Map.of());
            for (final Map.Entry<String, Object> entry : custom.entrySet()) {
                builder.addCustomProperty(entry.getKey(), entry.getValue());
            }
            payload = builder.build();
        }

        // The three-argument constructor defaults to IMMEDIATE priority and a one-day expiration, but it cannot carry a
        // push type, so unset fields reuse those same library defaults explicitly.
        final Instant expiration = scenario.containsKey("expiration")
            ? Instant.ofEpochSecond(((Number) scenario.get("expiration")).longValue())
            : Instant.now().plus(SimpleApnsPushNotification.DEFAULT_EXPIRATION_PERIOD);
        final DeliveryPriority priority = scenario.containsKey("priority")
            ? DeliveryPriority.getFromCode(((Number) scenario.get("priority")).intValue())
            : DeliveryPriority.IMMEDIATE;
        final String collapseId = (String) scenario.get("collapseId");
        final UUID apnsId = scenario.containsKey("apnsId") ? UUID.fromString((String) scenario.get("apnsId")) : null;

        return new SimpleApnsPushNotification(deviceToken, (String) scenario.get("topic"), payload, expiration,
            priority, pushType, collapseId, apnsId);
    }

    @SuppressWarnings("unchecked")
    private static void applyAps(final ApnsPayloadBuilder builder, final String key, final Object value)
        throws Unsupported {
        switch (key) {
            case "alert" -> applyAlert(builder, (Map<String, Object>) value);
            case "badge" -> builder.setBadgeNumber(((Number) value).intValue());
            case "sound" -> {
                if (value instanceof String name) {
                    builder.setSound(name);
                } else {
                    final Map<String, Object> sound = (Map<String, Object>) value;
                    builder.setSound((String) sound.get("name"), ((Number) sound.get("critical")).intValue() == 1,
                        ((Number) sound.get("volume")).doubleValue());
                }
            }
            case "content-available" -> builder.setContentAvailable(((Number) value).intValue() == 1);
            case "mutable-content" -> builder.setMutableContent(((Number) value).intValue() == 1);
            case "thread-id" -> builder.setThreadId((String) value);
            case "category" -> builder.setCategoryName((String) value);
            case "target-content-id" -> builder.setTargetContentId((String) value);
            case "interruption-level" -> builder.setInterruptionLevel(interruptionLevel((String) value));
            case "relevance-score" -> builder.setRelevanceScore(((Number) value).doubleValue());
            case "timestamp" -> builder.setTimestamp(Instant.ofEpochSecond(((Number) value).longValue()));
            case "event" -> builder.setEvent(liveActivityEvent((String) value));
            case "stale-date" -> builder.setStaleDate(Instant.ofEpochSecond(((Number) value).longValue()));
            case "dismissal-date" -> builder.setDismissalDate(Instant.ofEpochSecond(((Number) value).longValue()));
            case "content-state" -> builder.setContentState((Map<String, Object>) value);
            case "attributes-type" -> builder.setAttributesType((String) value);
            case "attributes" -> builder.setAttributes((Map<String, Object>) value);
            default -> throw new Unsupported("ApnsPayloadBuilder has no setter for aps." + key);
        }
    }

    @SuppressWarnings("unchecked")
    private static void applyAlert(final ApnsPayloadBuilder builder, final Map<String, Object> alert)
        throws Unsupported {
        for (final Map.Entry<String, Object> entry : alert.entrySet()) {
            final Object value = entry.getValue();
            switch (entry.getKey()) {
                case "title" -> builder.setAlertTitle((String) value);
                case "subtitle" -> builder.setAlertSubtitle((String) value);
                case "body" -> builder.setAlertBody((String) value);
                case "launch-image" -> builder.setLaunchImageFileName((String) value);
                // pushy sets a localized key together with its arguments, so each key consumes its args entry.
                case "title-loc-key" -> builder.setLocalizedAlertTitle((String) value,
                    args((List<Object>) alert.get("title-loc-args")));
                case "loc-key" -> builder.setLocalizedAlertMessage((String) value,
                    args((List<Object>) alert.get("loc-args")));
                case "subtitle-loc-key" -> builder.setLocalizedAlertSubtitle((String) value,
                    args((List<Object>) alert.get("subtitle-loc-args")));
                case "title-loc-args", "loc-args", "subtitle-loc-args" -> {
                    final String keyName = entry.getKey().replace("-args", "-key");
                    if (!alert.containsKey(keyName)) {
                        throw new Unsupported("pushy cannot set aps.alert." + entry.getKey() + " without " + keyName);
                    }
                }
                default -> throw new Unsupported("ApnsPayloadBuilder has no setter for aps.alert." + entry.getKey());
            }
        }
    }

    private static String[] args(final List<Object> values) {
        return values == null ? new String[0] : values.stream().map(String::valueOf).toArray(String[]::new);
    }

    private static InterruptionLevel interruptionLevel(final String value) throws Unsupported {
        for (final InterruptionLevel level : InterruptionLevel.values()) {
            if (level.getValue().equals(value)) {
                return level;
            }
        }
        throw new Unsupported("InterruptionLevel enum has no value '" + value + "'");
    }

    private static LiveActivityEvent liveActivityEvent(final String value) throws Unsupported {
        for (final LiveActivityEvent event : LiveActivityEvent.values()) {
            // LiveActivityEvent.getValue() is package-private; its constant names are the upper-cased wire values.
            if (event.name().toLowerCase(Locale.ROOT).equals(value)) {
                return event;
            }
        }
        throw new Unsupported("LiveActivityEvent enum has no value '" + value + "'");
    }

    private static Map<String, Object> decodeJwt(final String authorization) throws Exception {
        final String[] parts = authorization.replaceFirst("(?i)^bearer ", "").split("\\.");
        final Map<String, Object> jwt = new LinkedHashMap<>();
        jwt.put("header", new JsonParser().parseJsonObject(
            new String(Base64.getUrlDecoder().decode(parts[0]), StandardCharsets.UTF_8)));
        jwt.put("claims", new JsonParser().parseJsonObject(
            new String(Base64.getUrlDecoder().decode(parts[1]), StandardCharsets.UTF_8)));
        return jwt;
    }

    /** Deep-copies parsed JSON with sorted object keys so regenerated fixtures diff cleanly. */
    private static Object sorted(final Object value) {
        if (value instanceof Map<?, ?> map) {
            final Map<String, Object> result = new TreeMap<>();
            map.forEach((k, v) -> result.put(k.toString(), sorted(v)));
            return result;
        }
        if (value instanceof List<?> list) {
            return list.stream().map(Generate::sorted).toList();
        }
        return value;
    }

    private static void writeJson(final StringBuilder out, final Object value, final int depth) {
        if (value == null) {
            out.append("null");
        } else if (value instanceof Map<?, ?> map) {
            if (map.isEmpty()) {
                out.append("{}");
                return;
            }
            out.append("{\n");
            int i = 0;
            for (final Map.Entry<?, ?> entry : map.entrySet()) {
                indent(out, depth + 1);
                writeString(out, entry.getKey().toString());
                out.append(": ");
                writeJson(out, entry.getValue(), depth + 1);
                out.append(++i < map.size() ? ",\n" : "\n");
            }
            indent(out, depth);
            out.append('}');
        } else if (value instanceof List<?> list) {
            if (list.isEmpty()) {
                out.append("[]");
                return;
            }
            out.append("[\n");
            for (int i = 0; i < list.size(); i++) {
                indent(out, depth + 1);
                writeJson(out, list.get(i), depth + 1);
                out.append(i + 1 < list.size() ? ",\n" : "\n");
            }
            indent(out, depth);
            out.append(']');
        } else if (value instanceof String string) {
            writeString(out, string);
        } else if (value instanceof Number || value instanceof Boolean) {
            out.append(value);
        } else {
            throw new IllegalArgumentException("unexpected JSON value " + value.getClass());
        }
    }

    private static void writeString(final StringBuilder out, final String value) {
        // Reuse pushy's string escaping through its collection serializer, then drop the surrounding brackets.
        final String array = JsonSerializer.writeJsonTextAsString(List.of(value));
        out.append(array, 1, array.length() - 1);
    }

    private static void indent(final StringBuilder out, final int depth) {
        out.append("  ".repeat(depth));
    }
}

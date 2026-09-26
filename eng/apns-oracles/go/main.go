// Oracle generator for github.com/sideshow/apns2.
//
// Sends every scenario through a real apns2.Client whose HTTP transport records the request and answers 200, so the
// fixture reflects apns2's own header and payload building rather than a transcription of it.
package main

import (
	"bytes"
	"encoding/base64"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"log"
	"net/http"
	"os"
	"path/filepath"
	"runtime/debug"
	"strings"
	"time"

	"github.com/sideshow/apns2"
	"github.com/sideshow/apns2/payload"
	"github.com/sideshow/apns2/token"
)

const modulePath = "github.com/sideshow/apns2"

var headerNames = []string{
	"apns-push-type",
	"apns-topic",
	"apns-priority",
	"apns-expiration",
	"apns-collapse-id",
	"apns-id",
}

type spec struct {
	GeneratedAt string `json:"generatedAt"`
	DeviceToken string `json:"deviceToken"`
	JWT         struct {
		KeyFile string `json:"keyFile"`
		KeyID   string `json:"keyId"`
		TeamID  string `json:"teamId"`
	} `json:"jwt"`
	Scenarios []scenario `json:"scenarios"`
}

type scenario struct {
	ID         string                 `json:"id"`
	PushType   string                 `json:"pushType"`
	Topic      string                 `json:"topic"`
	Priority   *json.Number           `json:"priority"`
	Expiration *json.Number           `json:"expiration"`
	CollapseID *string                `json:"collapseId"`
	ApnsID     *string                `json:"apnsId"`
	Aps        map[string]interface{} `json:"aps"`
	Custom     map[string]interface{} `json:"custom"`
	Raw        map[string]interface{} `json:"raw"`
}

// orderedHeaders keeps the documented header order in the fixture; a nil pointer is a header apns2 did not send.
type orderedHeaders struct {
	PushType   *string `json:"apns-push-type"`
	Topic      *string `json:"apns-topic"`
	Priority   *string `json:"apns-priority"`
	Expiration *string `json:"apns-expiration"`
	CollapseID *string `json:"apns-collapse-id"`
	ApnsID     *string `json:"apns-id"`
}

type scenarioResult struct {
	ID                string          `json:"id"`
	Supported         bool            `json:"supported"`
	UnsupportedReason *string         `json:"unsupportedReason"`
	Headers           *orderedHeaders `json:"headers"`
	Payload           interface{}     `json:"payload"`
}

type jwtResult struct {
	Header map[string]interface{} `json:"header"`
	Claims map[string]interface{} `json:"claims"`
}

type output struct {
	Library     string           `json:"library"`
	Version     string           `json:"version"`
	GeneratedAt string           `json:"generatedAt"`
	Scenarios   []scenarioResult `json:"scenarios"`
	JWT         jwtResult        `json:"jwt"`
}

type capture struct {
	path    string
	headers http.Header
	body    []byte
}

// recorder is the apns2 client's transport: it records each request and answers as APNs does on success.
type recorder struct{ captures []capture }

func (r *recorder) RoundTrip(req *http.Request) (*http.Response, error) {
	var body []byte
	if req.Body != nil {
		var err error
		if body, err = io.ReadAll(req.Body); err != nil {
			return nil, err
		}
	}
	r.captures = append(r.captures, capture{path: req.URL.Path, headers: req.Header.Clone(), body: body})
	return &http.Response{
		StatusCode: http.StatusOK,
		Header:     http.Header{"Apns-Id": {"00000000-0000-0000-0000-000000000000"}},
		Body:       io.NopCloser(strings.NewReader("")),
		Request:    req,
	}, nil
}

type unsupportedError struct{ reason string }

func (e unsupportedError) Error() string { return e.reason }

func unsupported(format string, args ...interface{}) error {
	return unsupportedError{fmt.Sprintf(format, args...)}
}

func int64Of(v interface{}) int64 {
	n, err := v.(json.Number).Int64()
	if err != nil {
		log.Fatalf("expected an integer, got %v", v)
	}
	return n
}

func float64Of(v interface{}) float64 {
	f, err := v.(json.Number).Float64()
	if err != nil {
		log.Fatalf("expected a number, got %v", v)
	}
	return f
}

func stringsOf(v interface{}) []string {
	items := v.([]interface{})
	out := make([]string, len(items))
	for i, item := range items {
		out[i] = item.(string)
	}
	return out
}

func applyAlert(p *payload.Payload, alert map[string]interface{}) error {
	for key, value := range alert {
		switch key {
		case "title":
			p.AlertTitle(value.(string))
		case "subtitle":
			p.AlertSubtitle(value.(string))
		case "body":
			p.AlertBody(value.(string))
		case "title-loc-key":
			p.AlertTitleLocKey(value.(string))
		case "title-loc-args":
			p.AlertTitleLocArgs(stringsOf(value))
		case "loc-key":
			p.AlertLocKey(value.(string))
		case "loc-args":
			p.AlertLocArgs(stringsOf(value))
		case "subtitle-loc-key":
			p.AlertSubtitleLocKey(value.(string))
		case "subtitle-loc-args":
			p.AlertSubtitleLocArgs(stringsOf(value))
		case "launch-image":
			p.AlertLaunchImage(value.(string))
		default:
			return unsupported("payload.Payload has no builder method for aps.alert.%s", key)
		}
	}
	return nil
}

func applyAps(p *payload.Payload, key string, value interface{}) error {
	switch key {
	case "alert":
		if s, ok := value.(string); ok {
			p.Alert(s)
			return nil
		}
		return applyAlert(p, value.(map[string]interface{}))
	case "badge":
		p.Badge(int(int64Of(value)))
	case "sound":
		if s, ok := value.(string); ok {
			p.Sound(s)
			return nil
		}
		sound := value.(map[string]interface{})
		// SoundName and SoundVolume build apns2's sound dictionary, which always carries critical: 1.
		if int64Of(sound["critical"]) != 1 {
			return unsupported("payload.Payload always writes a sound dictionary with critical: 1")
		}
		p.SoundName(sound["name"].(string))
		p.SoundVolume(float32(float64Of(sound["volume"])))
	case "content-available":
		if int64Of(value) == 1 {
			p.ContentAvailable()
		}
	case "mutable-content":
		if int64Of(value) == 1 {
			p.MutableContent()
		}
	case "thread-id":
		p.ThreadID(value.(string))
	case "category":
		p.Category(value.(string))
	case "interruption-level":
		p.InterruptionLevel(payload.EInterruptionLevel(value.(string)))
	case "relevance-score":
		p.RelevanceScore(float32(float64Of(value)))
	case "timestamp":
		p.SetTimestamp(int64Of(value))
	case "event":
		// ELiveActivityEvent declares only update and end; its string type accepts "start" by conversion.
		p.SetEvent(payload.ELiveActivityEvent(value.(string)))
	case "stale-date":
		p.SetStaleDate(int64Of(value))
	case "dismissal-date":
		p.SetDismissalDate(int64Of(value))
	case "content-state":
		p.SetContentState(value.(map[string]interface{}))
	case "attributes-type":
		p.SetAttributesType(value.(string))
	case "attributes":
		p.SetAttributes(value.(map[string]interface{}))
	default:
		return unsupported("payload.Payload has no builder method for aps.%s", key)
	}
	return nil
}

func buildNotification(s scenario, deviceToken string) (*apns2.Notification, error) {
	n := &apns2.Notification{
		DeviceToken: deviceToken,
		Topic:       s.Topic,
		// EPushType is a string type, so push types without a declared constant (widgets, controls) convert directly.
		PushType: apns2.EPushType(s.PushType),
	}
	if s.Priority != nil {
		n.Priority = int(int64Of(*s.Priority))
	}
	if s.Expiration != nil {
		seconds := int64Of(*s.Expiration)
		// setHeaders writes apns-expiration only when Expiration is after the Unix epoch.
		if seconds <= 0 {
			return nil, unsupported("apns2 writes apns-expiration only when Expiration is after the Unix epoch, so %d cannot be sent", seconds)
		}
		n.Expiration = time.Unix(seconds, 0)
	}
	if s.CollapseID != nil {
		n.CollapseID = *s.CollapseID
	}
	if s.ApnsID != nil {
		n.ApnsID = *s.ApnsID
	}

	if s.Raw != nil {
		// Notification.Payload accepts raw JSON bytes and sends them unchanged.
		raw, err := json.Marshal(s.Raw)
		if err != nil {
			return nil, err
		}
		n.Payload = raw
		return n, nil
	}

	p := payload.NewPayload()
	for key, value := range s.Aps {
		if err := applyAps(p, key, value); err != nil {
			return nil, err
		}
	}
	for key, value := range s.Custom {
		p.Custom(key, value)
	}
	n.Payload = p
	return n, nil
}

func decodeJSON(data []byte) (interface{}, error) {
	decoder := json.NewDecoder(bytes.NewReader(data))
	decoder.UseNumber()
	var v interface{}
	err := decoder.Decode(&v)
	return v, err
}

func decodeJWT(authorization string) (jwtResult, error) {
	parts := strings.Split(strings.TrimPrefix(authorization, "bearer "), ".")
	if len(parts) != 3 {
		return jwtResult{}, fmt.Errorf("authorization is not a JWT: %q", authorization)
	}
	var result jwtResult
	for i, target := range []*map[string]interface{}{&result.Header, &result.Claims} {
		raw, err := base64.RawURLEncoding.DecodeString(parts[i])
		if err != nil {
			return jwtResult{}, err
		}
		v, err := decodeJSON(raw)
		if err != nil {
			return jwtResult{}, err
		}
		*target = v.(map[string]interface{})
	}
	return result, nil
}

func headerValue(h http.Header, name string) *string {
	if values, ok := h[http.CanonicalHeaderKey(name)]; ok && len(values) > 0 {
		v := values[0]
		return &v
	}
	return nil
}

func moduleVersion() string {
	info, ok := debug.ReadBuildInfo()
	if !ok {
		log.Fatal("build info unavailable")
	}
	for _, dep := range info.Deps {
		if dep.Path == modulePath {
			return dep.Version
		}
	}
	log.Fatalf("%s not found in build info", modulePath)
	return ""
}

func main() {
	oraclesDir := flag.String("oracles", "..", "the eng/apns-oracles directory")
	outPath := flag.String("out", "", "the fixture file to write")
	flag.Parse()
	if *outPath == "" {
		log.Fatal("-out is required")
	}

	raw, err := os.ReadFile(filepath.Join(*oraclesDir, "scenarios.json"))
	if err != nil {
		log.Fatal(err)
	}
	decoder := json.NewDecoder(bytes.NewReader(raw))
	decoder.UseNumber()
	var sp spec
	if err := decoder.Decode(&sp); err != nil {
		log.Fatal(err)
	}

	authKey, err := token.AuthKeyFromFile(filepath.Join(*oraclesDir, sp.JWT.KeyFile))
	if err != nil {
		log.Fatal(err)
	}
	rec := &recorder{}
	client := apns2.NewTokenClient(&token.Token{AuthKey: authKey, KeyID: sp.JWT.KeyID, TeamID: sp.JWT.TeamID})
	client.HTTPClient = &http.Client{Transport: rec}

	var results []scenarioResult
	var jwt *jwtResult
	for _, s := range sp.Scenarios {
		n, err := buildNotification(s, sp.DeviceToken)
		var unsup unsupportedError
		if errors.As(err, &unsup) {
			reason := unsup.reason
			results = append(results, scenarioResult{ID: s.ID, Supported: false, UnsupportedReason: &reason})
			continue
		}
		if err != nil {
			log.Fatalf("scenario %s: %v", s.ID, err)
		}

		before := len(rec.captures)
		response, err := client.Push(n)
		if err != nil || response.StatusCode != http.StatusOK || len(rec.captures) != before+1 {
			log.Fatalf("scenario %s: push failed: %v %+v", s.ID, err, response)
		}
		c := rec.captures[before]
		if c.path != "/3/device/"+sp.DeviceToken {
			log.Fatalf("scenario %s: unexpected path %s", s.ID, c.path)
		}

		var body interface{}
		if len(c.body) > 0 {
			if body, err = decodeJSON(c.body); err != nil {
				log.Fatalf("scenario %s: %v", s.ID, err)
			}
		}
		results = append(results, scenarioResult{
			ID:        s.ID,
			Supported: true,
			Headers: &orderedHeaders{
				PushType:   headerValue(c.headers, headerNames[0]),
				Topic:      headerValue(c.headers, headerNames[1]),
				Priority:   headerValue(c.headers, headerNames[2]),
				Expiration: headerValue(c.headers, headerNames[3]),
				CollapseID: headerValue(c.headers, headerNames[4]),
				ApnsID:     headerValue(c.headers, headerNames[5]),
			},
			Payload: body,
		})

		if jwt == nil {
			decoded, err := decodeJWT(c.headers.Get("Authorization"))
			if err != nil {
				log.Fatal(err)
			}
			jwt = &decoded
		}
	}

	// token.Generate always stamps iat from time.Now, so only its type is recorded.
	if _, err := jwt.Claims["iat"].(json.Number).Int64(); err != nil {
		log.Fatalf("apns2 JWT iat is not an integer: %v", jwt.Claims["iat"])
	}
	jwt.Claims["iat"] = "<number>"

	out := output{
		Library:     modulePath,
		Version:     moduleVersion(),
		GeneratedAt: sp.GeneratedAt,
		Scenarios:   results,
		JWT:         *jwt,
	}
	var buf bytes.Buffer
	encoder := json.NewEncoder(&buf)
	encoder.SetEscapeHTML(false)
	encoder.SetIndent("", "  ")
	if err := encoder.Encode(out); err != nil {
		log.Fatal(err)
	}
	if err := os.WriteFile(*outPath, buf.Bytes(), 0o644); err != nil {
		log.Fatal(err)
	}
	fmt.Printf("apns2 %s: wrote %d scenarios to %s\n", out.Version, len(results), *outPath)
}

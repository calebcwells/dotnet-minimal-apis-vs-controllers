# .NET Minimal APIs vs Controllers

[![CI](https://github.com/calebcwells/dotnet-minimal-apis-vs-controllers/actions/workflows/ci.yml/badge.svg)](https://github.com/calebcwells/dotnet-minimal-apis-vs-controllers/actions/workflows/ci.yml)

This .NET application exposes the same four operations three times: as an MVC controller, as
minimal APIs with static handlers, and as minimal APIs with instance methods on a DI-resolved
class. All three run over one shared service. A parity suite drives the same scenarios through all
three implementations and compares their observable HTTP behavior.

**The findings, up front:**

- Minimal APIs can do everything controllers can on the list people actually worry about:
  **grouping, authorization, dependency injection, error handling, and DataAnnotations
  validation**. `AddValidation()` in .NET 10 closed the last real gap.
- **Handlers do not have to be static.** A plain class with instance methods passes every parity
  test. There is [one genuine hazard](#the-one-real-hazard) in that style, and it is a hazard
  controllers cannot even express.
- **`Results<T1,T2>` only documents your success path.** Error responses still need
  `.ProducesProblem(...)`. This surprised me. My own endpoints were silently under-documented
  until I added the declarations.
- **Two real divergences remain.** Both are asserted by tests rather than described in prose.
- **Performance is the weakest argument for switching.** 20-23% less pipeline overhead and roughly
  20% fewer allocations sounds good until you notice the time gap is 3 to 5 microseconds.

## What this is and what it is not

This is a working, runnable comparison. Every claim above maps to a test you can run.

This is not a starter template, a library, or a recommendation to rewrite your controllers. All
three styles coexist in one application here without friction. That is the point.

**This requires the .NET 10 SDK.** The `AddValidation()` result is specific to .NET 10. On .NET 8
or 9 you would need a hand-rolled endpoint filter and the DataAnnotations row below would read
differently.

## How it is built

| Route prefix | Implementation | Handler shape |
|---|---|---|
| `/api/v1/controller/proposals` | [`Controllers/ProposalsController.cs`](src/ApiParity.Api/Controllers/ProposalsController.cs) | MVC controller |
| `/api/v1/minimal/proposals` | [`Endpoints/ProposalEndpoints.cs`](src/ApiParity.Api/Endpoints/ProposalEndpoints.cs) | static methods |
| `/api/v1/class/proposals` | [`Endpoints/ProposalEndpointModule.cs`](src/ApiParity.Api/Endpoints/ProposalEndpointModule.cs) | **instance methods on a regular class** |

The domain is a conference talk proposal. Speakers submit, reviewers accept. All three styles call
the same `IProposalService`, use the same authorization policies, share the same `ProblemDetails`
configuration, and appear in the same OpenAPI document. Only the transport layer varies, so any
difference in behavior is attributable to the style choice and nothing else.

The three error-mapping methods are written independently on purpose. If they called a shared
mapper, the parity tests would be asserting that shared code equals itself.

### Run it

Trust the development certificate first. You only need to do this once.

```bash
dotnet dev-certs https --trust
```

Run the parity suite. All 19 tests should pass.

```bash
dotnet test
```

Run the application. This serves https://localhost:7177 and opens the Scalar UI in your browser.

```bash
dotnet watch --project src/ApiParity.Api
```

Run the benchmarks. These must run in Release configuration.

```bash
dotnet run -c Release --project bench/ApiParity.Benchmarks
```

With the app running, https://localhost:7177/scalar/v1 serves interactive documentation
([Scalar](https://github.com/scalar/scalar)) over the one OpenAPI document that describes all
three styles side by side. The raw document is at https://localhost:7177/openapi/v1.json, and
plain-HTTP requests to port 5177 redirect there. `launchBrowser` in launchSettings is honored by
`dotnet watch` and the IDEs. Plain `dotnet run` serves the same URLs without opening a browser.

Two API keys are wired up: `speaker-key` (read only) and `reviewer-key` (read + accept). The UI
arrives with the reviewer key pre-filled so every operation works immediately. Switch it to
`speaker-key` to watch the accept endpoints start returning 403. That is the layered-policy
behavior the parity tests assert.

```bash
curl https://localhost:7177/api/v1/controller/proposals -H "X-Api-Key: speaker-key"
curl https://localhost:7177/api/v1/minimal/proposals    -H "X-Api-Key: speaker-key"
curl https://localhost:7177/api/v1/class/proposals      -H "X-Api-Key: speaker-key"
```

If curl rejects the certificate, run `dotnet dev-certs https --trust` or add `-k` to the command.

---

## The five concerns, answered

| Concern | Controllers | Minimal APIs | Same behavior? |
|---|---|---|---|
| **Grouping** | `[Route]` + class | `MapGroup(...)`, nestable ([`ProposalEndpoints.cs:29`](src/ApiParity.Api/Endpoints/ProposalEndpoints.cs#L29)) | Yes |
| **Authorization** | `[Authorize(Policy=…)]` | `.RequireAuthorization(policy)` ([`:34`](src/ApiParity.Api/Endpoints/ProposalEndpoints.cs#L34)) | Yes, identical 401/403 |
| **Dependency injection** | Constructor injection | Handler parameters ([`:89`](src/ApiParity.Api/Endpoints/ProposalEndpoints.cs#L89)), or constructor injection in the class-based style ([`ProposalEndpointModule.cs:25`](src/ApiParity.Api/Endpoints/ProposalEndpointModule.cs#L25)) | Yes |
| **Error handling** | `ControllerBase.Problem()` | `TypedResults.Problem()` ([`:159`](src/ApiParity.Api/Endpoints/ProposalEndpoints.cs#L159)) | Yes, identical bodies |
| **Data annotations** | Automatic via `[ApiController]` | Automatic via `AddValidation()` ([`Program.cs:35`](src/ApiParity.Api/Program.cs#L35)) | Yes, see the caveat below |

Below are the questions I expect from anyone evaluating the switch.

**Q: Can minimal APIs group endpoints the way a controller class does?**

**A:** Yes, and grouping is strictly more capable than `[Route]`. Groups nest, and anything
chained onto a group (prefix, tag, authorization, filters) is inherited by every endpoint in it.
You can also compose groups in code, for example looping over groups or applying a policy
conditionally. Attributes cannot do that.

**Q: Is minimal-API authorization a separate system?**

**A:** No. There is no separate minimal-API authorization stack. Every style names the same
registered policies and goes through the same `IAuthorizationService`. This repo layers a stricter
accept policy on top of a group-wide read policy and proves all three styles enforce both.

**Q: Does dependency injection require `[FromServices]`?**

**A:** No. Since .NET 7, any handler parameter the framework cannot bind from the route, query,
header, or body is resolved from the container.

**Q: Do DataAnnotations work on minimal APIs?**

**A:** Yes, on .NET 10. `AddValidation()` is new in .NET 10 and closes the last real gap. On
.NET 8 or 9 you had to hand-roll an endpoint filter to run DataAnnotations. On `net10.0` it is a
one-liner.

**Q: What happens to my action filters?**

**A:** They become endpoint filters, with one method instead of an executing/executed pair. Both
filter types are implemented here and a test asserts they produce the same header.

---

## Do handlers have to be static?

No. A handler is just a `Delegate`. All of these are valid:

```csharp
group.MapGet("/{id:guid}", GetAsync);                          // static method  (this repo)
group.MapGet("/{id:guid}", (Guid id) => /* … */);              // lambda
group.MapGet("/{id:guid}", (Module m, Guid id) => m.Get(id));  // instance method (this repo)
```

[`ProposalEndpointModule.cs`](src/ApiParity.Api/Endpoints/ProposalEndpointModule.cs) is an
ordinary class with a primary constructor and ordinary instance methods. It looks almost exactly
like the controller. Dependencies come in through the constructor and handlers use them. What it
does not inherit is `ControllerBase`, so there is no `HttpContext`/`ModelState`/`Url` surface, and
the class is directly constructible in a unit test. It passes every parity test the static version
does, and it costs no meaningful extra time or memory (see [Performance](#performance)).

The choice is a style preference.

| | Static handlers | Class with instance methods |
|---|---|---|
| Dependencies | Per-handler parameters | Constructor, shared across handlers |
| Reads like | A route table | A controller |
| Grouping shared state | Nothing to share | Fields available to every handler |
| Testing | Call the method | `new Module(fake)` then call |

### The one real hazard

There is exactly one trap, and it is worth knowing before anyone writes the second endpoint.

```csharp
var module = new ProposalEndpointModule(service);   // DON'T
group.MapGet("", module.ListAsync);
```

That compiles and works. The problem is that the delegate captures one instance at startup, so the
module is effectively a singleton holding whatever it was constructed with, regardless of how you
registered the type. With a scoped dependency (a `DbContext`, a per-request tenant accessor) that
is a captive-dependency bug that will not surface until concurrent traffic hits it.

The fix is to take the module as a handler parameter so DI resolves it per request. That is what
this repo does. Two diagnostic endpoints demonstrate the difference and a test asserts it.

```
GET /diagnostics/handler-lifetime/resolved   returns a new instanceId every request
GET /diagnostics/handler-lifetime/captured   returns the same instanceId forever
```

Controllers cannot express this mistake because MVC always activates them per request. This is a
genuinely new thing to know when moving to class-based minimal APIs.

---

## What you actually gain

1. **Less ceremony, and the ceremony that remains is real code.** The controller carries
   `[ApiController]`, `[Route]`, `[HttpGet]`, `[Consumes]`, and five `[ProducesResponseType]`
   attributes to say what a signature could say. The minimal handler's signature is the
   declaration.

2. **Response types the compiler checks.** `Results<Ok<ProposalResponse>, ProblemHttpResult>`
   means a handler returning anything else does not compile, and OpenAPI is generated from the
   signature. `[ProducesResponseType]` is an assertion nobody verifies. It drifts from the
   implementation silently, and in most codebases of any age it already has.

3. **Handlers that are trivially testable.** Static methods with explicit parameters, or a plain
   class you can `new` up. No `ControllerContext` and no `HttpContext` to fake.

4. **Measurably less pipeline overhead.** See [Performance](#performance). The difference is real
   but small in absolute terms.

5. **It is where the framework is going.** Minimal APIs get the new investment: `AddValidation()`,
   native OpenAPI, `TypedResults`, and request delegate generation for AOT. Controllers are stable
   and supported, not deprecated, and nobody is coming to take them away. The feature gap has been
   closing in one direction only.

---

## What you actually give up

Minimal APIs are not a strict superset of controllers. This is the honest list.

| Not available | Detail | When it matters |
|---|---|---|
| **Content negotiation** | No MVC output-formatter-based negotiation by default. Text, files, streams, HTML, and custom formats require explicit or custom results. | You rely on `Accept`-driven format selection. |
| **MVC action filters** | You get endpoint filters instead. Similar power, different API. Existing `IActionFilter`/`IAsyncActionFilter` implementations need rewriting. | At migration time. |
| **`ModelState`** | No `ModelState.IsValid` and no `ModelState.AddModelError`. | You mutate validation state in the handler. |
| **`ApplicationParts`** | No dynamic controller discovery from plugin assemblies. | You ship a plugin architecture. |
| **Views / Razor Pages** | No controller/Razor view-result integration. Minimal APIs can still return HTML explicitly. | You render server-side HTML through MVC or Razor Pages. |
| **Some third-party libraries** | Anything assuming the MVC pipeline (older OData, some Swashbuckle filter packages). | Check per library. |
| **Model binder providers** | Custom binding uses `BindAsync`/`TryParse` on the type rather than `IModelBinder`. | You have custom `IModelBinder`s. |
| **Per-request handler activation by default** | Class-based handlers must be resolved from DI, not captured. See [the hazard above](#the-one-real-hazard). | You use the class-based style. |

For a JSON API, most of that column is empty. The honest summary is not that the list is short.
It is that the list does not overlap much with what a typical JSON service does.

One more item is a trap rather than a missing feature. Problem responses are not inferred into
OpenAPI. `ProblemHttpResult` carries its status code at runtime, so `404`/`409`/`422` must be
declared with `.ProducesProblem(...)`, the exact analog of `[ProducesResponseType]`. I hit this
while building this repo. The minimal endpoints silently under-documented themselves until I added
the declarations. The compile-time guarantee covers the success path only. That nuance is written
into the code at
[`ProposalEndpoints.cs:38`](src/ApiParity.Api/Endpoints/ProposalEndpoints.cs#L38).

---

## The evidence

`dotnet test` runs 19 tests and all of them pass. The integration tests drive scenarios through
all three prefixes and compare status code, content type, and response body against the controller
baseline (with the per-request `traceId` stripped, since it is unique by design). A focused unit
regression also proves the OpenAPI comparison distinguishes response codes belonging to different
operations.

| Scenario | Asserted |
|---|---|
| Missing / unrecognized credentials | Identical `401` + body |
| Insufficient scope on a layered policy | Identical `403` + body |
| Sufficient scope | Identical `200`, state changed |
| Concurrent acceptance | Exactly one state transition succeeds |
| Not found | Identical `404` ProblemDetails |
| Conflicting state change | Identical `409` ProblemDetails |
| Business-rule failure | Identical `422` ProblemDetails |
| Unhandled exception | Identical `500`, no internal detail leaked |
| DataAnnotations violation | Identical `400`, same `type`/`title`/fields |
| Valid create | `201`, absolute `Location` that resolves |
| Query-string binding and filtering | Identical payload |
| Group-wide filter | All echo the correlation id |
| OpenAPI document | Same status codes per operation, all styles |
| Handler lifetime | DI-resolved is per-request; captured is not |

Apart from the two divergences below, the responses are byte-identical apart from `traceId`,
including the RFC `type` URIs.

### Documented divergences

There are two real differences. Each one is asserted by a test rather than described in prose, so
if a future .NET release closes one, the test fails and this document gets corrected. Both are
properties of the minimal-API stack, so they apply to the static and class-based styles alike.

**1. Validation reports fewer messages per field.** MVC reports every failing attribute on a
property. The .NET 10 minimal validator stops at the first failure per property. Given an empty
`SpeakerName` that violates both `[Required]` and `[StringLength]`:

```jsonc
// controller
"SpeakerName": ["The SpeakerName field is required.",
                "The field SpeakerName must be a string with a minimum length of 2 …"]
// both minimal styles
"SpeakerName": ["The SpeakerName field is required."]
```

Same fields flagged, same status, same shape, fewer messages each. This only matters if a client
renders every message per field.

**2. Content-type charset.** MVC appends `; charset=utf-8` to problem responses it writes itself.
The minimal writer does not. The media type is the same and UTF-8 is the JSON default, so this is
cosmetic, but a strict client-side string comparison would notice.

---

## Performance

```
BenchmarkDotNet v0.15.8 · .NET 10.0.9 (SDK 10.0.301) · Linux · AMD Ryzen AI MAX+ 395, 16 physical cores
InvocationCount=8192  IterationCount=30  WarmupCount=10
Measured 2026-08-02. No test guards this table. Compare the date against the last change to
routing, filters, or DI registration before trusting it.

| Method              | Categories        | Mean     | StdDev   | Ratio | Allocated | Alloc Ratio |
|-------------------- |------------------ |---------:|---------:|------:|----------:|------------:|
| controller          | GET one           | 13.34 us | 0.587 us |  1.00 |  22.30 KB |        1.00 |
| minimal API         | GET one           | 10.29 us | 1.124 us |  0.77 |  17.58 KB |        0.79 |
| minimal API (class) | GET one           | 10.70 us | 2.796 us |  0.80 |  17.93 KB |        0.80 |
|                     |                   |          |          |       |           |             |
| controller          | POST + validation | 21.25 us | 0.732 us |  1.00 |  29.96 KB |        1.00 |
| minimal API         | POST + validation | 16.42 us | 0.744 us |  0.77 |  23.59 KB |        0.79 |
| minimal API (class) | POST + validation | 16.54 us | 0.537 us |  0.78 |  23.93 KB |        0.80 |
```

Read this narrowly. It measures request-pipeline overhead only: in-memory `TestServer`, in-memory
store, no sockets, no TLS, no database. What is left is routing, filter invocation, model binding,
and result execution, which is precisely the part that differs.

What each row does: GET reads a stable seeded record. POST succeeds (201), so it covers model
binding, DataAnnotations, service dispatch, and route-based `Location` generation.
`CreatedAtRoute` on the controller runs against `LinkGenerator` in the minimal styles, which is
one of the few places the styles genuinely diverge. Responses and request content are disposed
inside the measured operation, and the singleton store is reset between iterations so its growth
cannot leak into the numbers. Measuring a rejected POST instead would keep the store bounded for
free, but it would quietly stop covering the created-result path this table exists to compare.

Static vs class-based is a wash. The class style resolves a scoped object per request, which costs
a fraction of a kilobyte and no reliable amount of time. The two minimal rows have swapped places
across runs and their error bars overlap. Do not read either ordering as a real difference. Pick
on readability, not on this table.

Do not quote this as an application speedup. The controller-to-minimal gap is a handful of
microseconds. Put one 20 ms database call behind any of these endpoints and it disappears into the
noise. The allocation figure (roughly 20% fewer bytes per request, stable across every run so far)
is the more durable result, and it matters at high throughput or under memory pressure, not on a
typical CRUD endpoint.

Re-run it before quoting it. These numbers are from one developer machine and one SDK build. Treat
the generated table from your own run as the evidence, not this checked-in copy. CI deliberately
does not run the benchmarks because shared runners are too noisy for a microsecond-scale
measurement to mean anything.

The performance argument is the weakest reason to switch. Boilerplate and compile-checked response
types are the real ones.

---

## Recommendation

Adopt minimal APIs for new endpoints and leave existing controllers alone.

Minimal APIs can do everything on the list, the parity is tested rather than asserted, and the
trade-offs do not touch a JSON API. A big-bang rewrite buys nothing. All three styles coexist in
one application without friction, which is exactly what this repo demonstrates. Convert an
existing controller when you are already changing it, not as a project.

On static vs class-based: either is fine, but pick one per codebase. If a team wants minimal APIs
to feel familiar coming from controllers, the class-based module is the gentler landing and the
diff against an existing controller is small. Make sure you register it scoped and resolve it as a
handler parameter.

---

## Notes

This repository was built with AI assistance (Claude). The findings, the test design, and the
editorial judgment about what is worth reporting are mine. A fair amount of the typing was not.

Corrections are welcome, especially if a future .NET release closes one of the documented
divergences. In that case a test here should already be failing.

[MIT](LICENSE).

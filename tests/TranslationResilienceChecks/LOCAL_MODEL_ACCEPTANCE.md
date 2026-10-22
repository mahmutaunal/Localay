# P5 local model acceptance

Status: **neither candidate selected**. P6 compiled and ran the shared portable adapter,
protocol and decision checks: LifetimeChecks 133 and TranslationResilienceChecks 91 passed.
These are fake HTTP/package fixtures, not model inference or semantic/performance evidence.
The required pinned macOS SDK was temporarily downloaded and hash-verified during P6;
no model/native dependency or additional NuGet package was downloaded. No local Windows
build/device test or workflow change was performed. Native packaging and all actual model
quality/end-to-end timing gates remain open.

`LocalModelChecks` is shared by the existing LifetimeChecks and TranslationResilienceChecks.
It exercises application code through fake HTTP and temporary manifest fixtures; its numeric
examples are policy tests, **not performance measurements**. `LocalModelDecision` evaluates
evidence only and never changes the configured provider.

| Candidate | Adapter | Runtime/package | Quality gate | <100ms end-to-end p95 | Decision |
|---|---|---|---|---|---|
| OPUS/Marian English→Turkish | C# + native C ABI source | Not built/installed; revision and asset hashes pending | Not evaluated | Not measured | Not eligible |
| TranslateGemma 4B English→Turkish | Existing Ollama service, specialized chat protocol | Not installed/probed; full digest and runtime version pending | Not evaluated | Not measured | Not eligible |

## TranslateGemma profile

Only the explicit `translategemma:4b` selection opts into the specialized path. Other Ollama
models retain the existing generic path. This profile uses the [official Ollama translation
template](https://ollama.com/library/translategemma), a single user message per source block,
two blank lines before source text, temperature 0, context 8192, output cap 1024 and keep-alive
5m. IDs, JSON, OCR positions and block separators are not model-generated. Real source
characters and line breaks are retained. `done=true` and `done_reason=stop` are required;
an incomplete output is discarded. A later failed block preserves earlier completed blocks as a partial ID response; it never fills a missing block with another result. Calls are sequential and cancellation prevents the next
block from starting. This can be slower for many blocks and is **not** presented as the
fastest engine. No simultaneous competing model jobs are introduced.

The [published 4b tag](https://ollama.com/library/translategemma:4b) currently describes
Gemma3 4.3B Q4_K_M. A mutable tag alone is not a pinned evaluation identity. Before accepting
results, record the installed full model digest, quantization, Ollama version, model chat
template and this application's TemplateVersion/options in a canonical profile artifact.
Use `translategemma-en-tr-v1:<SHA256-of-that-artifact>` as the reviewed identity. Do not reuse
an earlier review after any field changes. Restart Localay after changing the installed model;
hot replacement under the same Ollama tag is not supported by this experimental path.
No model download or automatic provider switch occurs in the adapter.

Both initial candidates translate independent blocks without adding the generic game-info,
previous-context or arbitrary custom prompt text to the trained input. Consequently domain
terminology/context fidelity is an **open quality gate**, not inherited from generic LLM
prompt behavior. Other language pairs and legacy clipboard payloads are explicitly rejected.

## Evidence rules

Every result binds to the exact pinned profile used in semantic review. Compare the same
input corpus, source geometry, cache state, presentation coverage and workload settings;
record them under one ComparisonSet. Synthetic stage replay and provider-only latency do
not qualify as `observed-end-to-end`. Cold load and warm-up are reported separately; failed,
cancelled, missing or hidden translations must still count against coverage. A short warm
provider call alone does not establish a short visible translation delay.

Semantic review is required: no critical errors, no quality regression against the existing
provider, and explicit review of all targeted risk classes. Reference wording is illustrative,
not a string-equality oracle. Unreviewed or nonzero critical-error results fail regardless of
speed. Zero failures on a finite corpus never proves all future translations 100% correct.

The latency gate requires complete coverage and warm **end-to-end** p95 strictly below
100ms. CPU time per frame, GPU time per frame (zero for CPU-only), and peak working set must
also be recorded. Among passing candidates, lower p95 wins. Within 10%, a candidate using
no more of any measured resource wins; conflicting resource tradeoffs need a recorded review,
not an invented aggregate score. Missing evidence retains the current provider.

## English→Turkish review starter set (all pending)

| Risk | Source | Example reference / review requirement |
|---|---|---|
| Negation | Do not delete the backup. | Yedek kopyayı silmeyin. |
| Scope | Not all changes were saved. | Değişikliklerin tümü kaydedilmedi; “hiçbiri” anlamına dönmemeli. |
| Condition | You can leave only after saving. | Yalnızca kaydettikten sonra ayrılabilirsiniz. |
| Number/unit | Remaining: 1.5 GB of 20 GB. | Kalan: 20 GB'nin 1,5 GB'si; oran ters çevrilmemeli. |
| Placeholder | Welcome, {player_name}! | Hoş geldin, {player_name}!; yer tutucu birebir korunmalı. |
| Format token | Health: %d / %d | Can: %d / %d; token sayısı ve sırası korunmalı. |
| Name | Ada defeated Atlas. | Ada, Atlas'ı yendi; kazanan/kaybeden değişmemeli. |
| Context | Charge the battery. / Charge the enemy. | Pili şarj edin. / Düşmana hücum edin. |
| UI ambiguity | Resume / Save / Load | Devam et / Kaydet / Yükle; eylem bağlamı incelenmeli. |
| Source separators | Code: 999 ##\|\|\|## | Kaynaktaki ayraç yeni blok sayılmamalı. |
| Multiline | Connection lost.\nRetry? | Bağlantı kesildi.\nYeniden denensin mi? |
| Domain glossary | Equip the Frost Blade. | Profil sözlüğü karşılığı ayrıca incelenmeli; adaptör sözlük desteği varsayılmamalı. |

Expand this set with existing application phrases and long/short blocks before quality
acceptance. Structural ID/EOS/hash checks are not a substitute for that review. Optional
native packaging, tokenizer equivalence, ABI validation and all measured gates remain open
for P6; no unavailable Windows device is imposed as a user prerequisite.

P6 source audit and remaining acceptance blockers are recorded in [PERFORMANCE_ACCEPTANCE.md](../../PERFORMANCE_ACCEPTANCE.md). No runtime pass is implied by that audit.

# Game translation provider decision

Updated 2026-10-22 after the user declined billing setup and any cost-bearing API test.
This supersedes the previous mandatory Cloud NMT profile.

2026-10-23: [the implementation direction](game-engine-direction.md) now selects
Hy-MT2 1.8B for a narrow local adapter integration, based on translation scope,
published language support and license. The local adapter and
game workflow integration are now implemented; see [setup](hymt2-setup.md).
Google web remains the default unless the user selects the local profile.
No model installation, inference or provider switch occurred during development.

## Existing Google web path

- Google Translate / Free Web Service, with no API key or Cloud billing setup.
- One provider; managed game batches cannot hedge or fall back to another provider.
- Game readiness rejects Cloud mode. The managed-batch dispatch guard also rejects
  Cloud before network work, protecting against configuration changes after readiness.
- Saved provider/mode preferences are not changed automatically. The default remains
  Google web; old Cloud profiles need an explicit switch to Free Web Service.
- Existing Cloud/local adapters remain for compatibility, outside the selected game path.
  No Cloud project, billing setup, key creation or actual Cloud request was performed.

## What remains limited

The web service exposes no pinned model revision. Selecting one provider does not
establish a fixed model family, perfect English–Turkish accuracy or predictable latency.
The existing web client's one-second request spacing and rate-limit backoff are retained.
This path has no Cloud API billing; internet access and service availability are still
required. Limits can delay or prevent translation. No unlimited-service promise is made.

Cloud/web cache identities stay separated. Google adapters do not consume conversation
history, so that history does not evict unchanged menu/HUD translations. Providers that
consume history retain their context isolation.

## Validation status

Portable technical tests check readiness, the dispatch/fallback policy, cancellation,
request/response contracts and cache isolation. Cloud transport fake-HTTP tests remain
as adapter regressions; they do not activate Cloud or measure inference quality.
Windows, GitHub Actions and real Cloud API tests are excluded.

English–Turkish semantic acceptance remains open. The offline
[60-case acceptance corpus and review tool](../tests/TranslationQualityChecks/README.md)
provide original game-like source text, acceptable meanings and frozen pilot criteria.
The [2026-10-22 single-block pilot](../tests/TranslationQualityChecks/results/ASSESSMENT-2026-10-22.md)
collected 60 real web translations without Cloud billing. Assistant review found six
major errors among the 50 nominally standalone examples; the pilot failed the frozen
acceptance criteria. The [batch comparison](../tests/TranslationQualityChecks/results/BATCH-ASSESSMENT-2026-10-22.md)
then returned the same 60 outputs using the production provider in four logical batches
and six real web requests; it did not improve meaning quality. No line shift was observed
in those outputs, but synthetic reverse-line tests expose the limit of positional mapping.
This is not an independent human benchmark. OCR, placement and actual game latency
were not measured. Reference translations or
fake responses are never counted as measured engine outputs.

The hardware-based Hy-MT2 candidate selection has been withdrawn. The
[local alternative research](local-translation-candidate.md) is an archived record,
not an instruction to download/run a model. The new implementation direction above
does not constitute measured quality or runtime acceptance. The [narrow game experience plan](game-experience-plan.md)
sets the next implementation scope; it does not schedule another inference campaign.

# OLX rental-terms processing

This slice interprets saved OLX rental descriptions through the shared AI SDK. It does not recapture listings,
overwrite source assertions, or publish a consumer listing. The console shows processor-owned fields and their
literal supporting quotes under **Processing results**.

The processor key is `rental-terms@2`; its database use case is `olx-rental-terms`. It extracts deposit,
minimum stay, advance rent and utility billing, preserving unknown, explicitly absent, present and conflicting
states. An unknown result means the value was not determined. Unsupported or ambiguous language can be rejected;
fixture coverage is not a measured live accuracy rate.

## Host and provider

AI is disabled by default and is only composed on the internal source host. Set these in the host's private
configuration, using an actual model ID available to the provider account:

| Setting | Value |
|---|---|
| `Harvest__Processing__Ai__Enabled` | `true` |
| `Ai__Providers__anthropic__Kind` | `anthropic` |
| `Ai__Providers__anthropic__ApiKey` | enter privately in the host's secret configuration |
| `Harvest__Processing__RentalTerms__Models__0` | `anthropic/<model-id>` |

The module uses the Harvest database with its own `ai` schema. Local startup applies the AI migrations; a deployed
host requires the normal migration/deploy procedure before startup verification. Neither host composition nor
registering a prompt enables a source or a schedule.

The provider route is an allowlist entry, not an API key. A missing provider configuration removes the route from
the ready list. Existing `rental-terms@1` retains its legacy OpenAI configuration for compatibility; new Claude
processing uses version 2.

## Register a private prompt

Prompt text lives in the shared AI database. Keep drafts outside the repository. The draft is a JSON
`PromptDraft` with `useCase: "olx-rental-terms"`, `systemTemplate`, `userTemplate`, optional `modelMatch`,
`parameters` and `note`. The user template must contain `{{description}}`. No other input variables are supplied.
`modelMatch` matches the model ID, without the provider prefix; omitting it makes the prompt available to all
configured routes. Parameters can include `maxOutputTokens`, `temperature` and `effort`.

On the configured source host, run:

```sh
dotnet HarvestSources.Api.dll rental-terms prompt --file /private/path/rental-terms-draft.json --by operator
```

`--file -` reads the same JSON from standard input. The command creates the versioned use case with the
code-owned output schema and `KeepOutputs=false`, then saves a new immutable prompt version. It prints only the
use case, version, content hash and ID. It refuses an existing incompatible use case rather than replacing it.
Pin the returned version on the Process stage. A missing version or a fallback to a different version is rejected
before a model call.

## First operator trial

1. In OLX **Settings**, explicitly enable **Model processing**.
2. On the intended section's Process stage, select **Rental terms with evidence**, the configured
   `provider/model` route and the saved positive prompt version.
3. Keep scheduling on demand and start with a small batch of saved captures. **Run now** starts processing in place.
4. Inspect the run's resolve, call and validation operations, then each post's **Processing results**. Compare
   every field and quote with the saved description, including unknowns and conflicts.
5. Review a labeled Russian/Uzbek sample before enabling unattended processing. Record accuracy, rejected outputs,
   tokens, priced cost and latency; `priced=false` means no price was configured, not a free call.

Changing source AI permission affects queued work when the worker starts processing it. An already started
processing operation may finish; opting out does not cancel a request already in progress. Successful results are
keyed by capture, processor, model route and prompt version. The same successful configuration is skipped; a new
capture or prompt pin is eligible.

The shared SDK handles bounded provider retries within one execution. Once a version-2 model execution fails or
its output fails evidence validation, that capture/configuration requires review and is not automatically
resubmitted by later runs. The console marks this state; inspect the error and its safe inference ID. After fixing
the prompt, save and select a new version to process again. Failures before a model call, such as a source opt-out,
remain eligible after configuration is repaired.

## Results and consumers

`GET /api/posts/{postId}/processing?containerId={containerId}&limit=50&sourceId={sourceId}` requires `posts:read`.
The optional `sourceId` filters results before the limit, including when several sources share a merged post.
It is also available
through `IPostProcessingBroker` and the frontend client. Results include capture/link/run IDs, current-capture
status, processor identity, completion time, review status and output. The output includes typed fields, quotes,
prompt/provider/model metadata, usage and latency.

Private containers stay tenant-scoped; tenant readers of the pool receive only pool-shareable source results.
Purged posts cannot be read or receive newly completed evidence. Results remain separate from canonical post
facts and the delivery event schema. A consumer holding only `events:read` cannot read this endpoint.

## Verification boundary

Development verification uses synthetic descriptions, fake model responses and disposable PostgreSQL. No provider
key is required for those tests. A real Claude run on saved OLX captures remains a separate acceptance step.

The package pin, `10.0.0-beta`, is published on NuGet. AI commit `d71898b` and tag `be-v10.0.0-beta`
passed [publish run 44042733179](https://github.com/wow-two-sdk-beta/wow-two-sdk-beta.ai/actions/runs/44042733179)
on November 17, 2026. All five packages restored from public NuGet in a clean consumer, which built and passed
its smoke check. SCFD verification uses that published version without an `AiSdkVersion` override.

Earlier on November 17, 2026, the isolated backend checks passed all 969 tests, including the disabled-stage settings
regressions. The frontend package passed 108 tests, ESLint, touched-file formatting, its package build and both
package/app type checks. The deployment-descriptor check was rerun successfully after adding its required
`engineering/deployment/deploy.yml` fixture to the isolated copy. No real provider call or live console deployment
was part of this verification. That earlier backend pass used `10.0.0-olxlocal2` from an isolated feed; it is
historical evidence, superseded by the public-package commit-preparation check recorded in the active version track.

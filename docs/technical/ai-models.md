# AI models (Claude)

Civiti uses one Claude model for every AI feature, in both backend services:

| Feature | Entry point | Runs in |
|---|---|---|
| Issue text enhancement (`POST /api/issues/enhance-text`) | `ClaudeEnhancementService.EnhanceTextAsync` | Civiti-Server |
| Petition drafting (`POST /api/issues/{id}/petition-body`) | `ClaudeEnhancementService.GeneratePetitionBodyAsync` | Civiti-Server |
| MCP `generate_petition_body` tool | same method, but template only in production (see below) | Civiti-MCP |

The web and mobile apps never name a model; they call these endpoints.

Content moderation is separate. It uses OpenAI's `omni-moderation-latest` alias (`OpenAIConfiguration`), which updates itself and is not covered here.

## Where the model is set

The model is set in one place: **`ClaudeConfiguration.Model`** in `Civiti.Infrastructure/Configuration/ClaudeConfiguration.cs`.

| Setting | Where | Current value |
|---|---|---|
| Model | `ClaudeConfiguration.Model` (code only) | `claude-sonnet-5-5` |
| Effort (`output_config.effort`) | `ClaudeConfiguration.Effort` (code only) | `low` |
| Thinking | not sent, so the model uses its default (adaptive, scaled by effort) | - |
| Max output tokens | `DefaultMaxTokens`, overridable with `CLAUDE_MAX_TOKENS` | 4096 |
| Request timeout | `DefaultTimeoutSeconds`, overridable with `CLAUDE_TIMEOUT_SECONDS` | 30s |
| Per-user rate limit | `DefaultRateLimitPerMinute`, overridable with `CLAUDE_RATE_LIMIT_PER_MINUTE` | 10/min |
| Petition cache TTL | `DefaultPetitionCacheHours`, overridable with `PETITION_CACHE_HOURS` | 24h |
| API key | `CLAUDE_API_KEY` env var, set on Civiti-Server and Civiti-MCP | - |

Civiti-MCP has the key too, so `generate_petition_body` calls the same model there as the web app does. Without the key, a service returns the deterministic template and logs a warning at startup.

The model and effort can't be overridden from env vars or appsettings, on purpose. A new model usually needs request changes too, and an env var that swaps only the model is how the code and the model drift apart. `CLAUDE_MODEL` / `Claude:Model` are ignored. If either is still set, both services log a startup warning; delete the variable.

Both services register the Claude stack through `AddClaudeEnhancement` (`Civiti.Infrastructure/Services/Claude/ClaudeServiceCollectionExtensions.cs`), so they always run the same model. Each service logs `Claude AI configured with model: … (effort …)` at startup, which is the quickest way to confirm what production runs.

Every request is built in `ClaudeEnhancementService.BuildMessageParameters`. A reply is used only when `stop_reason` is `end_turn` (`ReadCompletedText`). A refusal or a reply cut off at `max_tokens` falls back to the deterministic text instead of being shown or cached.

## Changing the model

1. **Read the migration notes for the new model** (Anthropic's model migration guide). Look for:
   - parameters the new model rejects: thinking types, sampling parameters, prefill, effort levels;
   - default changes: thinking on or off, default effort, `display`;
   - tokenizer changes, which affect `DefaultMaxTokens`;
   - new stop reasons or refusal categories.
2. **Check the SDK.** The project uses the community `Anthropic.SDK` package (`Civiti.Infrastructure.csproj`). Bump it if the new model needs request fields the pinned version can't send.
3. **Edit `ClaudeConfiguration.Model`.** Adjust `Effort` and `DefaultMaxTokens` if the notes call for it, and `BuildMessageParameters` if the request shape changes.
4. **Don't touch the petition cache.** The model is part of the petition content hash, so changing `Model` invalidates every cached petition core automatically.
5. **Test with real Romanian issues.** Try 3-5 issues with short, long and messy descriptions, in both features, and compare the output with the previous model. The prompts are in Romanian, and tone or length can shift between models.
6. **Watch latency.** The web petition modal gives up after 15s (`AI_TIMEOUT_MS` in the frontend's `email-modal.component.ts`) and falls back to its own template. The server finishes anyway and caches the result.
7. **Update the table above**, then deploy. Both services pick up the change from `master`.

To roll back, revert the commit. There's no env-var switch.

## History

| Date | Model | Effort | Notes |
|---|---|---|---|
| 2026-11 | `claude-sonnet-5-5` | `low` | Moved from Haiku 4.5. Added effort, a stop-reason check and the model in the petition hash; SDK 5.8.0 → 5.10.0. |
| earlier | `claude-haiku-4-5` | (default) | Could be overridden per service with `CLAUDE_MODEL`. |

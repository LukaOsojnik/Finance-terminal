# Adding a new LLM model or provider

This app routes two LLM **roles** to user-chosen providers:

- **Parsing & structuring** — filing extraction, the reviewer, chat, industry classification. Goes
  through `IChatLlm` (`ChatLlmRouter`), which dispatches to one `IChatProvider` per request based on
  the signed-in user's stored choice.
- **Web search** — counterparty discovery and private-company profiles. Always Perplexity, because
  its `sonar` models do web search + answer in a single call (you can't swap the model that "reads"
  the results — there is only one call). Only the sonar *variant* is selectable.

Per-user keys and choices live on the `UserApiKey` row (keys encrypted, model choices plaintext) and
are surfaced/edited on the **API Keys** page (`/Account/ApiKeys`).

The single source of truth for what's selectable is **`Services/ChatProviders.cs`**. Start there.

---

## Case 1 — Add a model to a provider that already exists

Example: DeepSeek ships a new `deepseek-v5-pro`, or you want another Perplexity sonar variant.

Edit **`Services/ChatProviders.cs`** only:

- **Parsing model** → add the id to that provider's `Models` list in `ChatProviders.Parsing`
  (first entry is the most capable; `DefaultModel` is what an unset user gets).
  ```csharp
  new(ChatProviderId.DeepSeek, "DeepSeek", "DeepSeek", "https://platform.deepseek.com/api_keys",
      ["deepseek-v5-pro", "deepseek-v4-pro", "deepseek-v4-flash"], "deepseek-v5-pro"),
  ```
- **Web-search model** → add the id to `ChatProviders.WebSearchModels` (and `DefaultWebSearchModel`
  if it should become the default).

That's it. The API Keys dropdowns (server render **and** the client-side `PARSING_MODELS` map in
`Views/Account/ApiKeys.cshtml`) and the router's default both read this list, so they can't drift.
No migration, no DI, no new code.

> Note: stored choices are validated against the catalog (`ChatProviders.ResolveModel`). If you ever
> **remove** a model a user had selected, they silently fall back to that provider's `DefaultModel`.

---

## Case 2 — Add a provider that is OpenAI-compatible

Most providers are (DeepSeek, Kimi/Moonshot, OpenAI, Mistral, Groq, xAI, …). They all speak
`POST /chat/completions` with `{ model, messages, max_tokens|max_completion_tokens, response_format }`
and a `Bearer` key — exactly what **`OpenAiCompatibleChatProvider`** already implements. You add
config + a key column + DI, but **no new transport class**.

Example: adding **Mistral**.

1. **Enum** — `Services/ChatProviders.cs`, add to `ChatProviderId`:
   ```csharp
   public enum ChatProviderId { DeepSeek, Kimi, OpenAi, Anthropic, Mistral }
   ```

2. **Catalog** — add a `ProviderInfo` to `ChatProviders.Parsing`:
   ```csharp
   new(ChatProviderId.Mistral, "Mistral", "Mistral", "https://console.mistral.ai/api-keys",
       ["mistral-large-latest", "mistral-small-latest"], "mistral-large-latest"),
   ```

3. **Base URL** — `appsettings.json`, add a section (no `ApiKey` — keys are bring-your-own):
   ```json
   "Mistral": { "BaseUrl": "https://api.mistral.ai/v1", "TimeoutSeconds": 120 },
   ```

4. **Key column** — `Models/Entities/UserApiKey.cs`, add `public string? MistralKey { get; set; }`,
   then generate a migration (see "Running the migration" below).

5. **Decrypted record** — `Services/IUserApiKeyProvider.cs`:
   - add `string? Mistral = null` to the `UserApiKeys` record params,
   - add a `ChatProviderId.Mistral => Mistral` arm to `KeyFor`,
   - add `string? Mistral = null, bool ClearMistral = false` to the `ApiKeyEdit` record.

6. **Persistence** — `Services/UserApiKeyProvider.cs`:
   - in `GetAsync`, pass `Decrypt(row.MistralKey)` in the right positional slot,
   - in `SaveAsync`, add `row.MistralKey = Apply(row.MistralKey, edit.Mistral, edit.ClearMistral);`

7. **Missing-key signal** — `Services/MissingApiKeyException.cs`, add a factory:
   ```csharp
   public static MissingApiKeyException Mistral() => new("Mistral", "Mistral API key missing");
   ```

8. **DI** — `Program.cs`, next to the Kimi/OpenAI registrations:
   ```csharp
   builder.Services.AddHttpClient("Mistral", c => ConfigureHttp(c, "Mistral"));
   builder.Services.AddScoped<IChatProvider>(sp => new OpenAiCompatibleChatProvider(
       sp.GetRequiredService<IHttpClientFactory>().CreateClient("Mistral"),
       sp.GetRequiredService<IUserApiKeyProvider>(), ChatProviderId.Mistral,
       k => k.Mistral, MissingApiKeyException.Mistral, "max_tokens"));
   ```
   The last arg is the cap parameter name — `"max_tokens"` for most; OpenAI's newer models need
   `"max_completion_tokens"`. Check the provider's docs.

9. **UI** — `Models/ViewModels/ApiKeysViewModel.cs` add `public ApiKeyStatus Mistral { get; set; } = new();`;
   `Controllers/AccountController.cs` set `Mistral = Status(keys.Mistral)` in `ApiKeys()`, add
   `string? mistralKey`, `bool clearMistral = false` params to `SaveApiKeys` and pass them into the
   `ApiKeyEdit`; `Views/Account/ApiKeys.cshtml` add one `KeyRow(...)` call with field name
   `"mistralKey"` / clear field `"clearMistral"`.

Done — the model dropdown, routing and key popup all pick it up from the catalog automatically.

---

## Case 3 — Add a provider that is NOT OpenAI-compatible

Example pattern: **Anthropic** (`Services/AnthropicChatProvider.cs`) — different auth header
(`x-api-key` + `anthropic-version`), top-level `system`, `content[]` block responses, mandatory
`max_tokens`. Google Gemini is another (its own REST shape).

Do everything in **Case 2 except step 8**, and instead write a dedicated transport:

- Create `Services/<Name>ChatProvider.cs` implementing `IChatProvider` (`Id`, `CompleteAsync`,
  `StreamAsync`). Use `AnthropicChatProvider` as the template — it shows how to absorb a different
  wire shape while still yielding the shared `ChatDelta` ("text" / "reasoning") fragments the chat UI
  renders, and how to map the streaming SSE events.
- Register it in `Program.cs`:
  ```csharp
  builder.Services.AddHttpClient("<Name>", c => ConfigureHttp(c, "<Name>"));
  builder.Services.AddScoped<IChatProvider>(sp => new <Name>ChatProvider(
      sp.GetRequiredService<IHttpClientFactory>().CreateClient("<Name>"),
      sp.GetRequiredService<IUserApiKeyProvider>()));
  ```

The router (`ChatLlmRouter`) keys providers by `IChatProvider.Id`, so any new `IChatProvider`
registered in DI is automatically routable — no router change needed.

---

## Running the migration

A model/key column change touches `UserApiKey`, so generate + apply a migration. `dotnet ef` isn't on
PATH; MySQL must be running. From the repo root:

```bash
# generate (the Testing env skips Pomelo's AutoDetect so it works even if MySQL is down)
ASPNETCORE_ENVIRONMENT=Testing ~/.dotnet/tools/dotnet-ef.exe migrations add Add<Name>ApiKey \
  --project simple-bloomberg-terminal/simple-bloomberg-terminal.csproj \
  --startup-project simple-bloomberg-terminal/simple-bloomberg-terminal.csproj

# apply (needs MySQL up)
~/.dotnet/tools/dotnet-ef.exe database update \
  --project simple-bloomberg-terminal/simple-bloomberg-terminal.csproj \
  --startup-project simple-bloomberg-terminal/simple-bloomberg-terminal.csproj
```

All EF NuGet packages are pinned to `9.0.0` (Pomelo pins EF Core 9 even on net10) — keep new ones at
`9.0.0`.

---

## Checklist

| Step | Case 1 | Case 2 (OpenAI-compatible) | Case 3 (custom) |
|------|:-:|:-:|:-:|
| `ChatProviders` catalog entry | ✅ | ✅ | ✅ |
| `ChatProviderId` enum value | — | ✅ | ✅ |
| `appsettings.json` BaseUrl | — | ✅ | ✅ |
| `UserApiKey` column + migration | — | ✅ | ✅ |
| `UserApiKeys` / `ApiKeyEdit` / provider persistence | — | ✅ | ✅ |
| `MissingApiKeyException` factory | — | ✅ | ✅ |
| ViewModel + controller + view key row | — | ✅ | ✅ |
| DI registration | — | ✅ (reuse `OpenAiCompatibleChatProvider`) | ✅ (new transport) |
| New `IChatProvider` class | — | — | ✅ |

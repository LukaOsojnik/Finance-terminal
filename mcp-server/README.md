# mcp-server

A **read-only** [Model Context Protocol](https://modelcontextprotocol.io) server that exposes the
Simple Bloomberg Terminal's stock data to an LLM agent. It is a **separate process** that talks to the
terminal over its HTTP API — it never touches the database directly and can never write (see *Security*).

## What it exposes

Tools are split by logical category so a weak model pulls only the slice it needs, and every result
carries an explicit `status` (`present` / `missing` / `unavailable` / `not_found` / `error`) so the
model reports *"this data isn't loaded"* instead of hallucinating around an empty field.

| Tool | Returns |
|---|---|
| `find_company` | Resolve a name/ticker/CIK → `companyId` (**call this first**) |
| `get_stock_profile` | Identity + GICS classification (sector / industry / sub-industry names) + headline figures + `missingFields` |
| `get_stock_financials` | Dated fiscal history (revenue, margins, income, cash flow) |
| `get_stock_risks` | Disclosed SEC Item 1A/7A risk factors |
| `get_stock_relationships` | Revenue sources (customers/segments) + cost sources (suppliers) |
| `get_stock_volume` | Weekly trading-volume series (recent weeks capped; full count reported) |
| `get_stock_filings` | Recent SEC EDGAR filings (live from SEC) |

## Configuration

| Setting | Env var (Railway) | Meaning |
|---|---|---|
| `Terminal:BaseUrl` | `Terminal__BaseUrl` | Base URL of the terminal API, e.g. the terminal service's Railway **private** URL (`http://<service>.railway.internal:8080/`) |
| `Terminal:ApiKey` | `Terminal__ApiKey` | Shared secret; **must equal** the terminal's `Mcp__ApiKey` |

The terminal side needs the matching secret:

| Terminal setting | Env var | Meaning |
|---|---|---|
| `Mcp:ApiKey` | `Mcp__ApiKey` | Enables the terminal's `X-Api-Key` auth scheme. When unset, the scheme is not registered and the terminal behaves exactly as before. |

The MCP server sends `X-Api-Key: <Terminal:ApiKey>` on every request; the terminal authenticates it as
the role-less `mcp-service` principal.

## Security

The MCP principal is **authenticated but holds no role**. The terminal's read endpoints are plain
`[Authorize]` (it satisfies them); every write is `[Authorize(Roles = "Admin,Manager")]` (it cannot).
So read-only is enforced by the terminal's own authorization, not merely by this server's choice of
tools. The shared key is compared in constant time (`Auth/ApiKeyAuthenticationHandler` in the terminal).

## Deploy on Railway

Both services live in one repo and deploy from one `git push`:

1. **Terminal service** — root `simple-bloomberg-terminal/`, its existing Dockerfile. Add env var
   `Mcp__ApiKey` (a long random secret).
2. **MCP service** — root `mcp-server/`, this Dockerfile. Add env vars `Terminal__BaseUrl`
   (the terminal's private URL) and `Terminal__ApiKey` (same value as `Mcp__ApiKey`).
3. Set each service's **Watch Paths** to its own folder so a change to one doesn't rebuild the other.

The container listens on `$PORT` (Railway-provided) via `ASPNETCORE_HTTP_PORTS`. The MCP endpoint is
served over Streamable HTTP at the service root.

## Run locally

```bash
# point at a locally running terminal
Terminal__BaseUrl="http://localhost:5000/" Terminal__ApiKey="dev-secret" dotnet run
# (set Mcp__ApiKey=dev-secret on the terminal too)
```

# sec2md sidecar

Python HTTP service that wraps [`sec2md`](https://github.com/lucasastorian/sec2md) so the C# app can
convert a SEC filing to clean markdown before the AI heading-triage in the extraction pipeline.

## Run

```bash
cd sec2md-service
pip install -r requirements.txt
uvicorn main:app --host 127.0.0.1 --port 8088
```

Optional: set a SEC User-Agent (EDGAR requires one):

```bash
SEC2MD_USER_AGENT="Your Name you@example.com"   # default uses the project owner's email
```

## API

`POST /convert`

```json
{ "url": "https://www.sec.gov/Archives/edgar/data/320193/000032019323000106/aapl-20230930.htm",
  "filing_type": "10-K" }
```

Response:

```json
{ "markdown": "**FORM 10-K** ..." }
```

`filing_type` is optional (reserved for a future `extract_sections` upgrade; full-markdown
conversion ignores it). On any sec2md/EDGAR failure the endpoint returns 502 and the C# client
falls back to raw HTML.

## How C# reaches it

`Sec2MdClient` (typed `HttpClient`) posts the EDGAR document URL it builds from the company CIK +
accession + primary document. Base URL is configured at `Sec2Md:BaseUrl` in `appsettings.json`
(default `http://localhost:8088`).

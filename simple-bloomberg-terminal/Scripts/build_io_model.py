#!/usr/bin/env python3
"""
Offline builder for the I-O core's model artifact (IoCore/Data/io_model_v1.json).

It turns a real BEA input-output Use table into the 11-sector GICS model the engine consumes:
the technical-coefficients matrix A, the value-added split (labor / capital / taxes), the baseline
final demand d, and an audit trail of the NAICS->GICS concordance.

Two real sources share one concordance (BEA codes are NAICS-based at every resolution):

  --source detail2002 --file <IOUseDetail.txt>
        BEA 2002 benchmark DETAIL use table (~400 industries). The committed model is built from
        this. Download: https://raw.githubusercontent.com/GreenDelta/usio/master/data/bea2002/redef/IOUseDetail.txt

  --source bea-api
        BEA API SUMMARY use table (~71 industries), latest annual release. Reads the API key and
        TableID/Year from appsettings.json ("Bea" section). Use this to refresh to current data.

The concordance is an APPROXIMATE modelling decision, not a lookup: GICS classifies by equity
market, NAICS by production, so manufacturing and services are split by judgement (see gics()).
"""

import argparse
import json
import os
import sys
import urllib.parse
import urllib.request

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_DIR = os.path.join(REPO, "IoCore", "Data")

# Canonical Sector enum order — must match Models/Enums/Sector.cs exactly.
SECTORS = [
    "ENERGY", "MATERIALS", "INDUSTRIALS", "CONSUMER_DISCRETIONARY", "CONSUMER_STAPLES",
    "HEALTH_CARE", "FINANCIALS", "INFORMATION_TECHNOLOGY", "COMMUNICATION_SERVICES",
    "UTILITIES", "REAL_ESTATE",
]
IDX = {s: i for i, s in enumerate(SECTORS)}


def gics(code):
    """Map a BEA industry/commodity code (NAICS-based) to a GICS sector, or None to exclude.

    None = drop from the matrix: government services, imports, scrap, ROW adjustments and final
    demand. Dropped intermediate rows become value-added 'leakage', which keeps colsum(A) < 1
    (the correct treatment for a DOMESTIC requirements matrix)."""
    c = (code or "").strip().upper()
    if not c:
        return None

    # BEA special-account codes (detail S00xxx, summary G* government).
    if c.startswith("S00101") or c.startswith("S00202"):
        return "UTILITIES"            # government electric utilities
    if c.startswith("S00800") or c.startswith("HS") or c.startswith("ORE"):
        return "REAL_ESTATE"          # owner-occupied dwellings / housing
    if c.startswith("S00201"):
        return "INDUSTRIALS"          # government passenger transit
    if c[0] in ("S", "G", "F", "V", "T"):
        return None                   # govt, final demand, value added, totals

    four, three, two = c[:4], c[:3], c[:2]

    # --- Manufacturing / service splits that GICS draws by market, not production (4-digit) ---
    if four == "3254":
        return "HEALTH_CARE"                                  # pharmaceuticals
    if four == "3391":
        return "HEALTH_CARE"                                  # medical equipment & supplies
    if four in ("3361", "3362", "3363"):
        return "CONSUMER_DISCRETIONARY"                       # motor vehicles & parts
    if four in ("3364", "3365", "3366", "3369"):
        return "INDUSTRIALS"                                  # aerospace, rail, ship, other transport
    if c.startswith("5112"):
        return "INFORMATION_TECHNOLOGY"                       # software publishers
    if c.startswith("5415"):
        return "INFORMATION_TECHNOLOGY"                       # computer systems design
    if c.startswith("518") or c.startswith("519") or three == "514":
        return "INFORMATION_TECHNOLOGY"                       # data processing, hosting, internet/other info services

    # --- Mining: oil/gas & coal -> Energy; metals & minerals -> Materials ---
    if three == "211":
        return "ENERGY"                                       # oil & gas extraction
    if c.startswith("2121"):
        return "ENERGY"                                       # coal mining (detail)
    if three == "212":
        return "MATERIALS"                                    # mining except oil & gas (metals & minerals)
    if c.startswith("213111") or c.startswith("213112"):
        return "ENERGY"                                       # oil & gas drilling / support
    if c.startswith("21311"):
        return "MATERIALS"                                    # support for other mining
    if three == "213":
        return "ENERGY"
    if three == "486":
        return "ENERGY"                                       # pipeline transport

    # --- Arts & entertainment vs leisure ---
    if three in ("711", "712"):
        return "COMMUNICATION_SERVICES"                       # performing arts, spectator sports, museums
    if three == "713":
        return "CONSUMER_DISCRETIONARY"                       # amusement, gambling, recreation

    # --- Agriculture / forestry ---
    if three == "113":
        return "MATERIALS"                                    # forestry & logging
    if two == "11":
        return "CONSUMER_STAPLES"                             # crop & animal farming, fishing, support

    # --- 2/3-digit NAICS sectors ---
    if two == "22":
        return "UTILITIES"
    if two == "23":
        return "INDUSTRIALS"                                  # construction
    if three in ("311", "312"):
        return "CONSUMER_STAPLES"                             # food, beverage, tobacco
    if three in ("313", "314", "315", "316"):
        return "CONSUMER_DISCRETIONARY"                       # textiles, apparel, leather
    if three in ("321", "322"):
        return "MATERIALS"                                    # wood, paper
    if three == "323":
        return "INDUSTRIALS"                                  # printing
    if three == "324":
        return "ENERGY"                                       # petroleum & coal products
    if three in ("325", "326", "327", "331"):
        return "MATERIALS"                                    # chemicals, plastics/rubber, mineral, primary metal
    if three in ("332", "333"):
        return "INDUSTRIALS"                                  # fabricated metal, machinery
    if three == "334":
        return "INFORMATION_TECHNOLOGY"                       # computer & electronic products
    if three == "335":
        return "INDUSTRIALS"                                  # electrical equipment
    if three == "336":
        return "INDUSTRIALS"                                  # other transport equipment
    if three == "337":
        return "CONSUMER_DISCRETIONARY"                       # furniture
    if three == "339":
        return "CONSUMER_DISCRETIONARY"                       # misc manufacturing
    if two == "42":
        return "INDUSTRIALS"                                  # wholesale trade
    if three == "445":
        return "CONSUMER_STAPLES"                             # food & beverage stores (staples retail)
    if c.startswith("4A") or two in ("44", "45"):
        return "CONSUMER_DISCRETIONARY"                       # retail trade
    if two in ("48", "49"):
        return "INDUSTRIALS"                                  # transportation & warehousing
    if two == "51":
        return "COMMUNICATION_SERVICES"                       # publishing, telecom, broadcasting, film
    if two == "52" or c.startswith("52A") or c.startswith("52B"):
        return "FINANCIALS"
    if two == "53":
        return "REAL_ESTATE"
    if two in ("54", "55", "56"):
        return "INDUSTRIALS"                                  # professional, management, admin services
    if two == "61":
        return "CONSUMER_DISCRETIONARY"                       # educational services
    if two == "62":
        return "HEALTH_CARE"                                  # health care & social assistance
    if two in ("71", "72", "81"):
        return "CONSUMER_DISCRETIONARY"                       # arts, accommodation/food, other services
    return None


def value_added_kind(rowcode):
    """Map a value-added row code to (labor|taxes|capital), or None if it is not a VA component."""
    c = (rowcode or "").strip().upper()
    if c.startswith("V001"):
        return "labor"        # compensation of employees
    if c.startswith("V002"):
        return "taxes"        # taxes on production & imports, less subsidies
    if c.startswith("V003"):
        return "capital"      # gross operating surplus
    return None


def aggregate(flows):
    """flows: iterable of (rowcode, colcode, value). Returns the assembled 11-sector model arrays
    plus the resolved concordance for auditing."""
    n = len(SECTORS)
    Z = [[0.0] * n for _ in range(n)]                 # intermediate sales: Z[i][j]
    labor = [0.0] * n
    taxes = [0.0] * n
    capital = [0.0] * n
    output = [0.0] * n                                # gross output per sector (column total)
    concordance = {}

    for rowcode, colcode, value in flows:
        if value == 0:
            continue
        col = gics(colcode)
        if col is None:
            continue                                  # column is not a kept industry
        concordance.setdefault(colcode.strip().upper(), col)
        j = IDX[col]

        va = value_added_kind(rowcode)
        if va is not None:
            output[j] += value
            if va == "labor":
                labor[j] += value
            elif va == "taxes":
                taxes[j] += value
            else:
                capital[j] += value
            continue

        rc = (rowcode or "").strip().upper()
        if rc[:1] in ("V", "T", "F"):
            continue                                  # other value-added aggregates / totals / final demand
        # Intermediate commodity input — counts toward output; enters A only if it maps to a sector.
        output[j] += value
        row = gics(rowcode)
        if row is not None:
            concordance.setdefault(rc, row)
            Z[IDX[row]][j] += value

    for j, s in enumerate(SECTORS):
        if output[j] <= 0:
            raise SystemExit(f"Sector {s} has non-positive gross output ({output[j]}); cannot build A.")

    # A[i][j] = Z[i][j] / output[j]; value-added intensities likewise per unit output.
    A = [[Z[i][j] / output[j] for j in range(n)] for i in range(n)]
    labor = [labor[j] / output[j] for j in range(n)]
    taxes = [taxes[j] / output[j] for j in range(n)]
    capital = [capital[j] / output[j] for j in range(n)]
    energy = [0.0] * n   # energy is endogenous (the ENERGY sector inside A); see README / adapter.

    # Baseline final demand by the Leontief identity d = x - A x (guarantees L d = x exactly, x>0).
    d = []
    for i in range(n):
        intermediate_sales = sum(Z[i][j] for j in range(n))
        d.append(output[i] - intermediate_sales)

    return A, labor, energy, capital, taxes, d, output, concordance


def spectral_radius(A, iters=1000, tol=1e-12):
    """Power iteration for ρ(A) — pure-python pre-check before the C# loader's full validation."""
    n = len(A)
    v = [1.0] * n
    last = 0.0
    for _ in range(iters):
        w = [sum(A[i][j] * v[j] for j in range(n)) for i in range(n)]
        norm = max(abs(x) for x in w) or 1.0
        v = [x / norm for x in w]
        if abs(norm - last) < tol:
            break
        last = norm
    return last


def report(A, output):
    n = len(A)
    print("Column sums of A (intermediate-input share, must be < 1):")
    for j, s in enumerate(SECTORS):
        col = sum(A[i][j] for i in range(n))
        flag = "" if col < 1.0 else "  <-- VIOLATES HAWKINS-SIMON"
        print(f"  {s:<24} {col:6.3f}   output={output[j]:14.1f}{flag}")
    print(f"Spectral radius rho(A) = {spectral_radius(A):.4f} (must be < 1)")


def write_outputs(A, labor, energy, capital, taxes, d, concordance, source):
    os.makedirs(OUT_DIR, exist_ok=True)
    model = {
        "version": 1,
        "source": source,
        "sectorOrder": SECTORS,
        "a": A,
        "labor": labor,
        "energy": energy,
        "capital": capital,
        "taxes": taxes,
        "d": d,
    }
    model_path = os.path.join(OUT_DIR, "io_model_v1.json")
    with open(model_path, "w", encoding="utf-8") as f:
        json.dump(model, f, indent=2)
    print(f"Wrote {model_path}")

    conc_path = os.path.join(OUT_DIR, "concordance_bea_to_gics_v1.json")
    with open(conc_path, "w", encoding="utf-8") as f:
        json.dump({"source": source, "mapping": dict(sorted(concordance.items()))}, f, indent=2)
    print(f"Wrote {conc_path} ({len(concordance)} BEA codes mapped)")


# --- Source: BEA 2002 detail use table (fixed-width text) ---

def flows_from_detail(path):
    with open(path, encoding="latin-1") as f:
        header = f.readline()
        com = header.find("Commodity")
        ind = header.find("Industry")
        pur = header.find("PurVal")
        ioy = header.find("IOYear")
        for line in f:
            commodity = line[com:com + 6].strip()
            industry = line[ind:ind + 6].strip()
            raw = line[pur:ioy].strip()
            if not raw or raw == ".":
                continue
            try:
                value = float(raw)
            except ValueError:
                continue
            yield commodity, industry, value


# --- Source: BEA API summary use table (JSON long format) ---
#
# TableID and Year are NOT hardcoded: the InputOutput TableID list is only available via the API
# itself (GetParameterValues), so the script discovers them. If Bea:UseTableId is blank it auto-
# picks the Use table at the requested aggregation; if Bea:Year is blank it picks the latest year.
# Run `--list-tables` to print every InputOutput table + available years for your key.

def _bea_get(cfg, method, extra):
    if not cfg.get("ApiKey"):
        raise SystemExit("Bea:ApiKey is empty in appsettings.json. Add your BEA API key and retry.")
    params = {"UserID": cfg["ApiKey"], "method": method, "DataSetName": "InputOutput",
              "ResultFormat": "JSON", **extra}
    url = cfg.get("BaseUrl", "https://apps.bea.gov/api/data") + "?" + urllib.parse.urlencode(params)
    with urllib.request.urlopen(url, timeout=180) as resp:
        payload = json.load(resp)
    api = payload.get("BEAAPI", {})
    err = api.get("Error") or api.get("Results", {})
    if isinstance(err, dict) and err.get("Error"):
        raise SystemExit(f"BEA API error: {err.get('Error')}")
    return api["Results"]


def _table_values(cfg):
    res = _bea_get(cfg, "GetParameterValues", {"ParameterName": "TableID"})
    rows = res["ParamValue"] if isinstance(res, dict) else res[0]["ParamValue"]
    # each row: {"Key": "...", "Desc": "..."}
    return [(r.get("Key"), r.get("Desc", "")) for r in rows]


def list_tables(cfg):
    print("InputOutput tables available for your key:")
    for key, desc in _table_values(cfg):
        print(f"  {key:>5}  {desc}")


def _resolve_table_id(cfg):
    if cfg.get("UseTableId"):
        return cfg["UseTableId"]
    # Auto-pick the "Use of Commodities by Industries" table, preferring Summary level + After
    # Redefinitions + Producer prices. Falls back to the first Use table found.
    tables = _table_values(cfg)
    def score(desc):
        d = desc.lower()
        if "use" not in d:
            return -1
        s = 0
        if "summary" in d: s += 4
        if "after redefinition" in d: s += 2
        if "producer" in d: s += 1
        if "sector" in d: s += 1  # sector (15) acceptable if no summary
        return s
    ranked = sorted(((score(desc), key, desc) for key, desc in tables), reverse=True)
    if not ranked or ranked[0][0] < 0:
        raise SystemExit("Could not find a 'Use' table; run --list-tables and set Bea:UseTableId.")
    _, key, desc = ranked[0]
    print(f"Auto-picked TableID={key}: {desc}")
    return key


def _resolve_year(cfg, table_id):
    if cfg.get("Year"):
        return cfg["Year"]
    res = _bea_get(cfg, "GetParameterValuesFiltered",
                   {"TargetParameter": "Year", "TableID": table_id})
    rows = res["ParamValue"] if isinstance(res, dict) else res[0]["ParamValue"]
    years = sorted(int(r.get("Key") or r.get("Year")) for r in rows)
    print(f"Auto-picked latest Year={years[-1]} (available {years[0]}–{years[-1]})")
    return str(years[-1])


def flows_from_bea_api():
    cfg = _read_bea_config()
    table_id = _resolve_table_id(cfg)
    year = _resolve_year(cfg, table_id)
    print(f"Fetching BEA InputOutput GetData: TableID={table_id} Year={year}")
    res = _bea_get(cfg, "GetData", {"TableID": table_id, "Year": year})
    rows = res["Data"] if isinstance(res, dict) else res[0]["Data"]
    for r in rows:
        row_code = r.get("RowCode") or r.get("rowCode")
        col_code = r.get("ColCode") or r.get("colCode")
        raw = str(r.get("DataValue") or "0").replace(",", "").strip()
        try:
            value = float(raw)
        except ValueError:
            value = 0.0
        yield row_code, col_code, value


def _read_bea_config():
    path = os.path.join(REPO, "appsettings.json")
    with open(path, encoding="utf-8") as f:
        return json.load(f).get("Bea", {})


def main():
    ap = argparse.ArgumentParser(description="Build IoCore/Data/io_model_v1.json from a BEA Use table.")
    ap.add_argument("--source", choices=["detail2002", "bea-api"], default="detail2002")
    ap.add_argument("--file", help="Path to IOUseDetail.txt (required for --source detail2002)")
    ap.add_argument("--list-tables", action="store_true",
                    help="Print every InputOutput TableID + description for your key, then exit.")
    args = ap.parse_args()

    if args.list_tables:
        list_tables(_read_bea_config())
        return

    if args.source == "detail2002":
        if not args.file or not os.path.exists(args.file):
            sys.exit("--source detail2002 requires --file <IOUseDetail.txt>")
        flows = list(flows_from_detail(args.file))
        source = "BEA 2002 benchmark detail use table (redefinitions); aggregated to 11 GICS sectors"
    else:
        flows = list(flows_from_bea_api())
        source = "BEA API summary use table; aggregated to 11 GICS sectors"

    A, labor, energy, capital, taxes, d, output, concordance = aggregate(flows)
    report(A, output)
    write_outputs(A, labor, energy, capital, taxes, d, concordance, source)


if __name__ == "__main__":
    main()

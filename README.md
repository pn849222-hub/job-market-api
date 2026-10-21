# Job Market API

A read-only REST API over Vietnamese IT job postings, built with ASP.NET Core
and SQLite.

The data is produced by a separate pipeline,
[vn-it-job-market](https://github.com/pn849222-hub/vn-it-job-market), which
collects, cleans, and analyzes the postings. This repo is the serving layer: the
two together go from a live website to a documented API.

---

## Run it

```bash
python3 tools/seed-demo-db.py      # creates data/jobs.db with synthetic postings
dotnet run --project src/JobMarketApi
```

Then open <http://localhost:5000/swagger>.

Or with Docker. The image generates its own synthetic database while it builds,
so it runs with nothing mounted:

```bash
docker build -t job-market-api .
docker run --rm -p 8080:8080 job-market-api    # http://localhost:8080/swagger
```

`docker compose up --build` mounts `data/jobs.db` over the built-in one, so seed
it first (or copy in the pipeline's output). On hosts that set `PORT`, such as
Render, the API listens on that port instead of 8080.

To serve the real dataset instead, run the pipeline in the companion repo and
point `Database__Path` at its `data/processed/jobs.db`.

### Why a seed script rather than a committed database

The collected postings belong to the job board and the employers who wrote them,
so neither repo republishes them. The seed script generates invented rows with
the same schema and the same shape as the real data — most postings hiding their
salary, Ho Chi Minh City dominating — so the endpoints can be demonstrated
without redistributing anything.

---

## Endpoints

| Method | Route | Purpose |
|--------|-------|---------|
| `GET` | `/health` | Liveness: the process is up |
| `GET` | `/health/ready` | Readiness: the database is reachable and populated |
| `GET` | `/api/jobs` | Postings, filtered and paged |
| `GET` | `/api/skills/top` | Skills ranked by how many postings name them |
| `GET` | `/api/stats` | Posting count, salary disclosure rate, city mix |

### `GET /api/jobs`

| Parameter | Type | Default | Notes |
|-----------|------|---------|-------|
| `city` | string | — | Exact match, e.g. `Hà Nội` |
| `skill` | string | — | Case-insensitive, e.g. `python` |
| `seniority` | string | — | `Junior`, `Middle`, `Senior`, … |
| `withSalaryOnly` | bool | `false` | Exclude postings that hide the figure |
| `page` | int | `1` | |
| `pageSize` | int | `20` | Capped at 100 |

```bash
curl "http://localhost:5000/api/jobs?skill=python&withSalaryOnly=true&pageSize=2"
```

```json
{
  "items": [
    {
      "jobId": "1009",
      "title": "Lead Data Engineer",
      "company": "Demo Tech 6",
      "city": "Hà Nội",
      "seniority": "Lead/Principal",
      "employmentType": "FULL_TIME",
      "postedDate": "2026-10-14",
      "salaryMinUsd": 2273.56,
      "salaryMaxUsd": 3687.72,
      "salaryDisclosed": true,
      "sourceUrl": "https://example.test/jobs/1009",
      "skills": ["AWS", "Apache Spark", "Python", "SQL"]
    }
  ],
  "page": 1,
  "pageSize": 2,
  "total": 9,
  "totalPages": 5,
  "hasNext": true
}
```

---

## How it is put together

```
src/JobMarketApi/
├── Program.cs              minimal API: endpoints, DI, Swagger, health probes
├── Data/
│   ├── IJobRepository.cs   the interface the endpoints depend on
│   └── JobRepository.cs    Dapper queries against SQLite
└── Models/Dtos.cs          request and response shapes

tests/JobMarketApi.Tests/   xUnit, 11 tests against a temporary database
tools/seed-demo-db.py       generates the demo database
```

Decisions worth noting:

- **The database is opened read-only.** The analysis pipeline owns writes; this
  service has no reason to hold a write handle, so it does not ask for one.
- **Filters are composed, not concatenated.** Each optional filter appends a
  clause and a parameter, so every value stays parameterised.
- **Skills come back in one round trip.** `group_concat` in a correlated
  subquery avoids a query per job.
- **Paging reports what is left.** `totalPages` and `hasNext` are computed so a
  client does not have to guess, and `pageSize` is capped so one request cannot
  ask for everything.
- **Two health endpoints, not one.** Liveness answers "is the process up";
  readiness answers "can it actually serve", which is the one an orchestrator
  should gate traffic on.
- **The container health check is the app itself.** `dotnet JobMarketApi.dll
  --healthcheck` probes `/health/ready` and exits 0 or 1, so the runtime image
  needs no `curl`.

---

## Tests

```bash
dotnet test
```

11 tests. Each builds its own throwaway SQLite file with known rows, so the
assertions do not depend on whatever the live crawl happened to find. They cover
the statistics maths, case-insensitive skill filtering, the salary filter, paging
boundaries, the page-size cap, and readiness when the database is missing.

---

## Tech

.NET 10 · ASP.NET Core Minimal APIs · Dapper · SQLite · Swagger / OpenAPI ·
Docker · xUnit

Built by [Nguyen Vu Truong Phuc](https://github.com/pn849222-hub).

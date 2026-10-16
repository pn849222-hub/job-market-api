#!/usr/bin/env python3
"""Build data/jobs.db with synthetic postings so the API runs out of the box.

The real database comes from the vn-it-job-market pipeline, whose collected
postings are not redistributed. The rows here are invented — they share the
schema and the shape of the real data (most postings hide their salary, Ho Chi
Minh City dominates) so the endpoints can be demonstrated honestly.

    python3 tools/seed-demo-db.py
"""

import random
import sqlite3
from pathlib import Path

DB_PATH = Path(__file__).resolve().parent.parent / "data" / "jobs.db"

SCHEMA = """
CREATE TABLE jobs (
    job_id            TEXT PRIMARY KEY,
    title             TEXT,
    company           TEXT,
    city              TEXT,
    district          TEXT,
    seniority         TEXT,
    employment_type   TEXT,
    posted_date       TEXT,
    valid_through     TEXT,
    months_experience INTEGER,
    salary_currency   TEXT,
    salary_min_usd    REAL,
    salary_max_usd    REAL,
    salary_raw        TEXT,
    salary_disclosed  INTEGER,
    source_url        TEXT
);
CREATE TABLE job_skills (
    job_id TEXT REFERENCES jobs(job_id),
    skill  TEXT,
    PRIMARY KEY (job_id, skill)
);
CREATE INDEX idx_skill ON job_skills(skill);
"""

ROLES = [
    ("Backend Developer", [".NET", "C#", "PostgreSQL", "Docker"]),
    ("Frontend Developer", ["React", "TypeScript", "HTML/CSS"]),
    ("Data Engineer", ["Python", "SQL", "Apache Spark", "AWS"]),
    ("Data Analyst", ["SQL", "Python", "Power BI"]),
    ("DevOps Engineer", ["Docker", "Kubernetes", "CI/CD", "Linux"]),
    ("QA Engineer", ["QA/QC", "Automation Test", "Tester"]),
    ("Mobile Developer", ["Kotlin", "Android", "React Native"]),
    ("Business Analyst", ["Business Analysis", "Agile", "SQL"]),
]

SENIORITY = [
    ("Intern/Fresher", "Intern", 0, 300, 500),
    ("Junior", "Junior", 10, 500, 900),
    ("Middle", "Middle", 24, 900, 1600),
    ("Senior", "Senior", 37, 1500, 2600),
    ("Lead/Principal", "Lead", 60, 2500, 4000),
]

# Weighted to match the real distribution: the market is concentrated in the south.
CITIES = ["Hồ Chí Minh"] * 6 + ["Hà Nội"] * 3 + ["Đà Nẵng"]
COMPANIES = [f"Demo Tech {n}" for n in range(1, 21)]
HIDDEN_SALARY_TEXT = ["You'll love it", "Competitive", "Negotiable"]


def main():
    random.seed(42)
    DB_PATH.parent.mkdir(parents=True, exist_ok=True)
    DB_PATH.unlink(missing_ok=True)

    con = sqlite3.connect(DB_PATH)
    con.executescript(SCHEMA)

    jobs, skills = [], []
    for i in range(1, 121):
        role, role_skills = random.choice(ROLES)
        label, prefix, months, floor, ceiling = random.choice(SENIORITY)
        city = random.choice(CITIES)

        # About a third publish a figure, as in the real listings.
        disclosed = random.random() < 0.35
        low = round(floor * random.uniform(0.9, 1.1), 2) if disclosed else None
        high = round(ceiling * random.uniform(0.9, 1.1), 2) if disclosed else None
        raw = f"{low:,.0f} - {high:,.0f} USD" if disclosed else random.choice(HIDDEN_SALARY_TEXT)

        job_id = str(1000 + i)
        jobs.append(
            (
                job_id,
                f"{prefix} {role}",
                random.choice(COMPANIES),
                city,
                None,
                label,
                "FULL_TIME",
                f"2026-10-{random.randint(1, 16):02d}",
                None,
                months,
                "USD",
                low,
                high,
                raw,
                int(disclosed),
                f"https://example.test/jobs/{job_id}",
            )
        )

        chosen = set(random.sample(role_skills, k=random.randint(2, len(role_skills))))
        if random.random() < 0.5:
            chosen.add("English")
        skills.extend((job_id, skill) for skill in chosen)

    con.executemany("INSERT INTO jobs VALUES (" + ",".join("?" * 16) + ")", jobs)
    con.executemany("INSERT OR IGNORE INTO job_skills VALUES (?,?)", skills)
    con.commit()
    con.close()

    print(f"wrote {DB_PATH} with {len(jobs)} demo postings")


if __name__ == "__main__":
    main()

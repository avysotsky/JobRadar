# Running JobRadar with persistent PostgreSQL (Windows and Linux)

## Requirements

- Docker Engine + Docker Compose v2, or Docker Desktop on Windows.
- A machine that stays powered on when scheduled scans should run.
- Internet access to public vacancy platforms; honor their usage rules.
- Separate local `.env` file with a strong PostgreSQL password; do not commit or paste the password into ChatGPT.

## Start

```powershell
git clone https://github.com/avysotsky/JobRadar.git
cd JobRadar
Copy-Item .env.example .env
# Edit .env: replace REPLACE_WITH_RANDOM_LONG_PASSWORD with a secure random password.
docker compose config --quiet
docker compose up --build -d
docker compose logs -f collector
```

The collector starts its **scheduled loop**, with scans at 08:00, 13:00 and 19:00 **Europe/Kyiv**. It does not necessarily run a scan at container startup. PostgreSQL is **not published to the host network**. Two named Docker volumes persist the database and JSON reports across application restarts.

## On-demand run and status

```powershell
docker compose run --rm collector --once
docker compose run --rm collector --pending-status
docker compose run --rm collector --retry-failed
docker compose run --rm collector --export-jsonl
```

Do **not** run an on-demand crawl concurrently with the scheduled collector before implementing a database-wide scan lease. Use `docker compose stop collector` before manual `--once`, then `docker compose start collector`. Read-only status or exports are safe. Jooble remains separate and opt-in; do not run it automatically.

## Obtain reports for review

```powershell
docker compose cp collector:/app/reports ./jobradar-reports
```

After a run, the named report volume contains timestamped crawl reports. You can attach exported JSONL to a ChatGPT conversation for full-text matching. This environment cannot connect directly to a local Docker volume on your Windows computer.

## Upgrade and recovery

```powershell
git pull
docker compose up --build -d
docker compose logs --tail=100 collector
```

Persistent state survives image and container replacement in the named volumes. To back up, use PostgreSQL `pg_dump` through `docker compose exec database` and store the dump privately. **Never run `docker compose down -v` unless you intentionally want to delete the database and reports.**

## Known limits

- No guarantee that a platform exposes all listings through RSS or public APIs.
- The collector uses rate limits and logs partial results; it does not bypass CAPTCHA/403/authentication.
- A host, PostgreSQL credentials and first deployment still need the user's action.
- Docker and database storage alone do not implement alerts or remote access from ChatGPT.

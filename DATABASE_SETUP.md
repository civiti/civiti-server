# Database Setup Guide

## Local Development Setup

### Option 1: Using Docker (Recommended)

1. **Start PostgreSQL with Docker Compose:**
   ```bash
   docker-compose up -d
   ```

2. **Verify it's running:**
   ```bash
   docker ps
   ```

3. **Connection details:**
   - Host: `localhost`
   - Port: `5433` (mapped from container's 5432)
   - Database: `civica_dev`
   - Username: `civica`
   - Password: `civica123`

### Option 2: Local PostgreSQL Installation

1. **Install PostgreSQL** (if not already installed):
   - Windows: Download from https://www.postgresql.org/download/windows/
   - Mac: `brew install postgresql`
   - Linux: `sudo apt-get install postgresql`

2. **Create database and user:**
   ```sql
   CREATE USER civica WITH PASSWORD 'civica123';
   CREATE DATABASE civica_dev OWNER civica;
   GRANT ALL PRIVILEGES ON DATABASE civica_dev TO civica;
   ```

### Option 3: Using WSL2 (Windows)

Since you're on WSL2, you can install PostgreSQL directly:

```bash
# Install PostgreSQL
sudo apt update
sudo apt install postgresql postgresql-contrib

# Start PostgreSQL service
sudo service postgresql start

# Create user and database
sudo -u postgres psql
CREATE USER civica WITH PASSWORD 'civica123';
CREATE DATABASE civica_dev OWNER civica;
\q
```

## Running Migrations

Once PostgreSQL is running, the app will automatically run migrations on startup.

To manually run migrations:

```bash
cd Civica.Api
dotnet ef database update
```

## Supabase Configuration

Update `appsettings.Development.json` with your Supabase credentials:

```json
"Supabase": {
  "Url": "https://your-project.supabase.co",
  "AnonKey": "your-actual-anon-key"
}
```

## Connection String Format

The app supports both formats:
- Standard: `Host=localhost;Port=5433;Database=civica_dev;Username=civica;Password=civica123`
- Railway URL: `postgres://civica:civica123@localhost:5433/civica_dev`

## Troubleshooting

1. **Connection refused:**
   - Ensure PostgreSQL is running: `docker ps` or `sudo service postgresql status`
   - Check if port 5432 is not already in use

2. **Authentication failed:**
   - Verify username/password in connection string
   - Check PostgreSQL logs: `docker logs civica-db`

3. **Database does not exist:**
   - The app will create it on first migration
   - Or create manually using psql
   - Note: When using psql, always specify the database: `docker exec civica-db psql -U civica -d civica_dev`

## Production (Railway)

Railway automatically provides `DATABASE_URL` when you add PostgreSQL service. No manual configuration needed.
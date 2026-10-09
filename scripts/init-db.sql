-- Execute as PostgreSQL admin once (psql -U postgres -f scripts/init-db.sql)
CREATE USER jobradar WITH PASSWORD 'CHANGE_ME';
CREATE DATABASE jobradar OWNER jobradar;
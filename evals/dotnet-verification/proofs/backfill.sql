-- Diagnostic fixture: execute only in an empty disposable PostgreSQL database.
CREATE TABLE "Users" ("Id" int PRIMARY KEY, "FullName" text, "DisplayName" text);
INSERT INTO "Users" SELECT n, 'user-' || n, NULL FROM generate_series(1,12000) n;
CREATE TABLE audit_tx (tx bigint);
CREATE FUNCTION audit_update() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN INSERT INTO audit_tx VALUES(txid_current()); RETURN NEW; END $$;
CREATE TRIGGER update_audit AFTER UPDATE ON "Users" FOR EACH ROW EXECUTE FUNCTION audit_update();
DO $$
DECLARE
    batch_size INT := 5000;
    rows_updated INT;
BEGIN
    LOOP
        UPDATE "Users"
        SET "DisplayName" = "FullName"
        WHERE "Id" IN (
            SELECT "Id" FROM "Users"
            WHERE "DisplayName" IS NULL AND "FullName" IS NOT NULL
            LIMIT batch_size
        );

        GET DIAGNOSTICS rows_updated = ROW_COUNT;
        EXIT WHEN rows_updated = 0;

        -- Prevent lock escalation and replication lag
        PERFORM pg_sleep(0.1);
    END LOOP;
END $$;

DO $$ BEGIN
 IF (SELECT count(*) FROM audit_tx) <> 12000 OR (SELECT count(DISTINCT tx) FROM audit_tx) <> 1 THEN
  RAISE EXCEPTION 'Backfill transaction observation differs';
 END IF;
END $$;
SELECT count(*) AS updated_rows, count(DISTINCT tx) AS distinct_transactions FROM audit_tx;
SELECT count(*) AS missing_names FROM "Users" WHERE "DisplayName" IS NULL;
CREATE TABLE "Customers" ("Id" int PRIMARY KEY);
CREATE TABLE "Orders" ("Id" int PRIMARY KEY, "CustomerId" int);
BEGIN;
ALTER TABLE "Orders" ADD CONSTRAINT fk_orders_customer FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id") NOT VALID;
SELECT mode, granted FROM pg_locks WHERE pid=pg_backend_pid() AND relation='"Orders"'::regclass;
ROLLBACK;
SELECT version();

"""
main.py
-------
Entry point for the Procurement & Sales Test-Data Agent.

Pipeline:
  1. Connect to SQL Server (pyodbc) using config.DB.
  2. Introspect the LIVE schema (schema_introspector) and compute the
     topological insertion order, so every master table a transaction
     table references is fully populated -- and its identity values
     captured -- before that transaction table is generated.
  3. For each table, in dependency order:
        a. DataGenerator builds the in-memory rows (LLM-assisted for
           lexical fields; 100% deterministic/rule-based for the
           domain-mandated columns -- Contact, Location, Reason,
           InvoiceNumber, statuses, dates, FKs).
        b. Rows are re-validated against domain_rules validators as a
           final zero-hallucination safety net, right before they can
           ever reach an INSERT statement.
        c. Rows are bulk-inserted in parameter-safe chunks using an
           OUTPUT-INTO clause, so the REAL, engine-assigned identity
           values flow straight back into AgentState's PK pools for
           the next table's foreign keys (never a random guessed int).
  4. Prints a final per-table + grand-total row-count summary.

Usage:
    python main.py                # generate + insert into the live DB
    python main.py --reset        # wipe existing data (FK-safe order) first
    python main.py --dry-run      # generate + validate only, never inserts
"""
import argparse
import logging
import sys
import time
from typing import Dict, List

import pyodbc

import domain_rules as dr
from config import DB, LLM, DATA_YEAR
from schema_introspector import SchemaIntrospector, TableInfo, topological_order
from state import AgentState
from llm_client import LLMClient
from generators import DataGenerator

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
    datefmt="%H:%M:%S",
)
logger = logging.getLogger("agent.main")

# SQL Server allows ~2100 parameters per statement -- stay well under that
# regardless of how many columns a table has.
MAX_SQL_PARAMS = 2000


# ---------------------------------------------------------------------------
# Validation safety net -- runs AFTER generation, BEFORE any INSERT.
# Column-name based, so it applies uniformly no matter which table it is.
# ---------------------------------------------------------------------------
def revalidate_rows(table_info: TableInfo, rows: List[Dict], state: AgentState) -> List[Dict]:
    for row in rows:
        for col_name in list(row.keys()):
            lname = col_name.lower()
            value = row[col_name]
            if "contact" in lname and not dr.validate_contact(str(value)):
                row[col_name] = dr.random_vendor_contact()
            elif ("location" in lname or "address" in lname) and not dr.validate_location(str(value)):
                row[col_name] = dr.random_egypt_location()
            elif "reason" in lname and not dr.validate_reason(str(value)):
                row[col_name] = dr.random_credit_note_reason()
            elif "invoicenumber" in lname.replace("_", "") and not dr.validate_invoice_number(str(value)):
                seq = state.next_sequence(f"invnum::{table_info.name}::fixup")
                row[col_name] = dr.format_invoice_number(seq, DATA_YEAR)
    return rows


_STRING_TYPES = ("nvarchar", "varchar", "char", "nchar", "text", "ntext", "uniqueidentifier")


def assign_non_identity_pk(table_info: TableInfo, rows: List[Dict], state: AgentState, table_name: str) -> None:
    """For a single-column PK that is NOT an identity column, guarantee a
    globally-unique value ourselves -- in place, before any INSERT.

    Two real cases show up in practice:
      - An INTEGER surrogate key with no IDENTITY property: we assign
        1, 2, 3, ... sequentially (only when the generator left it empty).
      - A STRING key such as ASP.NET Identity's AspNetUsers.Id / AspNetRoles.Id
        (nvarchar(450)): the generic column resolver's word-fallback is
        NOT guaranteed unique and WILL collide once you generate more than
        a couple hundred rows (this is exactly what caused "Violation of
        PRIMARY KEY constraint ... duplicate key value is (Accept)" on
        ApplicationUsers). We need guaranteed uniqueness here, but a raw
        UUID also isn't what's wanted (illegible, "1, 2, 3, ..." is the
        actual requirement for Roles/ApplicationUsers). So for a string PK
        we ALWAYS overwrite with the row's 1-based position as a plain
        digit string ("1", "2", "3", ...) -- unique by construction (a
        single incrementing counter per table), human-readable, and still
        a valid value for an nvarchar column, regardless of whatever the
        generator put there. This preserves row order (row order is never
        reshuffled here), so e.g. Role row 0 ("Admin") reliably gets Id
        "1", matching USER_ROLE_CODES order.
    """
    pk_col = table_info.single_pk
    if not pk_col or pk_col in table_info.identity_columns:
        return
    pk_column_info = table_info.columns.get(pk_col)
    is_string_pk = bool(pk_column_info) and pk_column_info.data_type in _STRING_TYPES
    for row in rows:
        if is_string_pk:
            row[pk_col] = str(state.next_sequence(f"pk::{table_name}"))
        elif row.get(pk_col) in (None, ""):
            row[pk_col] = state.next_sequence(f"pk::{table_name}")


# ---------------------------------------------------------------------------
# Insertion
# ---------------------------------------------------------------------------
def insert_rows(conn: "pyodbc.Connection", table_info: TableInfo, rows: List[Dict]) -> List[int]:
    """Insert `rows` in parameter-safe chunks.

    When the table has an identity column, an OUTPUT-INTO-@table-variable
    clause captures the engine-assigned values for that chunk (this form
    also works even if the table has triggers, unlike a plain client-facing
    OUTPUT). Order of returned IDs is not guaranteed to match input row
    order for a multi-row VALUES insert -- that's fine here, because
    generators.py already only needs *a* valid PK from the pool, never an
    exact row correlation.
    """
    if not rows:
        return []

    columns = list(rows[0].keys())
    col_list = ", ".join(f"[{c}]" for c in columns)
    id_col = table_info.identity_columns[0] if table_info.identity_columns else None

    per_row_params = max(len(columns), 1)
    chunk_size = max(1, MAX_SQL_PARAMS // per_row_params)

    cursor = conn.cursor()
    inserted_ids: List[int] = []

    for start in range(0, len(rows), chunk_size):
        chunk = rows[start:start + chunk_size]
        values_sql = ", ".join("(" + ", ".join(["?"] * len(columns)) + ")" for _ in chunk)
        params = [row.get(c) for row in chunk for c in columns]

        if id_col:
            # SET NOCOUNT ON suppresses the "(N rows affected)" messages that
            # DECLARE/INSERT would otherwise emit as interim, non-query
            # "results" -- that's exactly what trips up pyodbc's
            # cursor.fetchall() right after execute() with "No results.
            # Previous SQL was not a query."
            sql = (
                "SET NOCOUNT ON;\n"
                "DECLARE @Ids TABLE (Id BIGINT);\n"
                f"INSERT INTO [{table_info.name}] ({col_list}) "
                f"OUTPUT INSERTED.[{id_col}] INTO @Ids "
                f"VALUES {values_sql};\n"
                "SELECT Id FROM @Ids;"
            )
            cursor.execute(sql, params)
            inserted_ids.extend(sorted(_fetch_final_resultset(cursor)))
        else:
            sql = f"INSERT INTO [{table_info.name}] ({col_list}) VALUES {values_sql}"
            cursor.execute(sql, params)

    conn.commit()
    return inserted_ids


def _fetch_final_resultset(cursor: "pyodbc.Cursor") -> List[int]:
    """Multi-statement batches (SET NOCOUNT ON; DECLARE ...; INSERT ...;
    SELECT ...;) can still leave the cursor positioned on an interim,
    non-query result depending on driver/server version. Walk forward
    with nextset() until fetchall() succeeds, or there's genuinely
    nothing left -- never let this raise and abort the whole pipeline."""
    while True:
        try:
            return [r.Id for r in cursor.fetchall()]
        except pyodbc.ProgrammingError:
            if not cursor.nextset():
                return []


# ---------------------------------------------------------------------------
# Reset (FK-safe wipe + identity reseed so new IDs start at 1 again)
# ---------------------------------------------------------------------------
def reset_database(conn: "pyodbc.Connection", order: List[str]) -> None:
    logger.warning("Resetting existing data (children before parents)...")
    cursor = conn.cursor()
    for table in reversed(order):
        try:
            cursor.execute(f"DELETE FROM [{table}]")
            logger.info(f"  cleared {table} ({cursor.rowcount} rows)")
        except pyodbc.Error as exc:
            conn.rollback()
            logger.error(f"  could not clear {table}: {exc}")
            raise
    conn.commit()

    for table in order:
        try:
            cursor.execute(
                "IF OBJECTPROPERTY(OBJECT_ID(?), 'TableHasIdentity') = 1 "
                f"DBCC CHECKIDENT ('{table}', RESEED, 0)",
                table,
            )
        except pyodbc.Error:
            pass  # table has no identity column -- nothing to reseed
    conn.commit()
    logger.info("Reset complete -- identities reseeded to start at 1.")



def register_product_prices(generator: DataGenerator, table_info: TableInfo, rows: List[Dict], state: AgentState) -> None:
    """Product price per REAL product id, so SO lines use the real selling
    price and PO lines use a purchase cost that is always BELOW it."""
    price_col = next((c.name for c in table_info.columns.values() if "price" in c.name.lower()), None)
    pool = state.get_pk_pool(table_info.name)
    if not price_col or len(pool) != len(rows):
        return
    state.extra["product_price"] = {pid: float(r[price_col]) for pid, r in zip(pool, rows)}


def apply_header_totals(conn: "pyodbc.Connection", generator: DataGenerator) -> None:
    """PurchaseOrder / SalesOrder header total = sum(quantity x price) of ITS lines."""
    cur = conn.cursor()
    cur.fast_executemany = True
    for ti, total_col, pk_col, pairs in generator.header_total_updates():
        if not pairs:
            continue
        cur.executemany(f"UPDATE [{ti.name}] SET [{total_col}] = ? WHERE [{pk_col}] = ?", pairs)
        logger.info(f"{ti.name}: rolled up {len(pairs)} header totals from line items.")
    conn.commit()

# ---------------------------------------------------------------------------
# Pipeline
# ---------------------------------------------------------------------------
def run(dry_run: bool = False, reset: bool = False) -> None:
    logger.info(f"Connecting to {DB.database} on {DB.server} ...")
    conn = pyodbc.connect(DB.connection_string, autocommit=False)

    try:
        introspector = SchemaIntrospector(conn)
        schema = introspector.build_schema()
        if not schema:
            raise RuntimeError(
                "No base tables found -- check DB_SERVER/DB_NAME/driver in your .env."
            )
        order = topological_order(schema)
        logger.info("Insertion order: " + " -> ".join(order))

        if reset and not dry_run:
            reset_database(conn, order)

        state = AgentState(data_year=DATA_YEAR)
        llm = LLMClient()
        if llm.enabled:
            logger.info(f"LLM assist ENABLED ({LLM.provider}/{LLM.model}) for lexical fields.")
        else:
            logger.info("LLM assist DISABLED -- falling back to deterministic/Faker generation.")

        generator = DataGenerator(schema, state, llm)

        summary: Dict[str, int] = {}
        total_start = time.time()

        for table_name in order:
            table_info = schema[table_name]
            t0 = time.time()

            rows = generator.generate_for_table(table_info)
            if not rows:
                logger.info(f"{table_name}: 0 rows generated, skipping.")
                summary[table_name] = 0
                continue

            rows = revalidate_rows(table_info, rows, state)

            assign_non_identity_pk(table_info, rows, state, table_name)
            pk_col = table_info.single_pk

            if dry_run:
                logger.info(f"{table_name}: generated {len(rows)} rows (dry-run, not inserted).")
                summary[table_name] = len(rows)
                if pk_col and pk_col not in table_info.identity_columns:
                    for row in rows:
                        if pk_col in row:
                            state.register_pk(table_name, row[pk_col])
                else:
                    for i in range(len(rows)):
                        state.register_pk(table_name, i + 1)
                if table_name.lower() in ("product", "products"):
                    register_product_prices(generator, table_info, rows, state)
                continue

            inserted_ids = insert_rows(conn, table_info, rows)

            id_col = table_info.identity_columns[0] if table_info.identity_columns else pk_col
            if id_col:
                if inserted_ids:
                    for pk in inserted_ids:
                        state.register_pk(table_name, pk)
                elif pk_col:
                    for row in rows:
                        if pk_col in row:
                            state.register_pk(table_name, row[pk_col])

            if table_name.lower() in ("product", "products"):
                register_product_prices(generator, table_info, rows, state)

            elapsed = time.time() - t0
            logger.info(f"{table_name}: inserted {len(rows)} rows in {elapsed:.1f}s")
            summary[table_name] = len(rows)

        if not dry_run:
            apply_header_totals(conn, generator)

        total_elapsed = time.time() - total_start
        total_rows = sum(summary.values())

        logger.info("=" * 60)
        logger.info("SUMMARY")
        logger.info("=" * 60)
        for table_name, count in summary.items():
            logger.info(f"  {table_name:<28} {count:>8,} rows")
        logger.info("-" * 60)
        logger.info(f"  {'TOTAL':<28} {total_rows:>8,} rows")
        logger.info(f"  completed in {total_elapsed:.1f}s")

    except Exception:
        conn.rollback()
        logger.exception("Pipeline failed -- transaction rolled back.")
        raise
    finally:
        conn.close()


def main() -> None:
    parser = argparse.ArgumentParser(description="Procurement & Sales Test-Data Agent")
    parser.add_argument(
        "--reset", action="store_true",
        help="Wipe existing data (FK-safe order, identities reseeded to 1) before generating.",
    )
    parser.add_argument(
        "--dry-run", action="store_true",
        help="Generate and validate rows only; never touches the database.",
    )
    args = parser.parse_args()

    try:
        run(dry_run=args.dry_run, reset=args.reset)
    except Exception as exc:
        logger.error(f"Fatal error: {exc}")
        sys.exit(1)


if __name__ == "__main__":
    main()
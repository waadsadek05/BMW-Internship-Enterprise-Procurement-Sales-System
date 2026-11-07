"""
schema_introspector.py
-----------------------
Dynamically inspects the LIVE SQL Server schema — columns, data types,
identity flags, primary keys, and foreign keys — and builds a
dependency graph between tables. The topological sort of that graph is
the agent's generation/insertion order: every master table a
transaction table references is fully populated first.

Nothing here is hardcoded to this particular database; it will work
against any SQL Server schema reachable over the given pyodbc
connection.
"""
from dataclasses import dataclass, field
from typing import Dict, List, Optional
import pyodbc


@dataclass
class ColumnInfo:
    name: str
    data_type: str
    max_length: Optional[int]
    is_nullable: bool
    is_identity: bool
    numeric_precision: Optional[int] = None
    numeric_scale: Optional[int] = None


@dataclass
class ForeignKey:
    fk_table: str
    fk_column: str
    ref_table: str
    ref_column: str


@dataclass
class TableInfo:
    name: str
    columns: Dict[str, ColumnInfo] = field(default_factory=dict)
    primary_key: List[str] = field(default_factory=list)
    foreign_keys: List[ForeignKey] = field(default_factory=list)

    @property
    def identity_columns(self) -> List[str]:
        return [c.name for c in self.columns.values() if c.is_identity]

    @property
    def single_pk(self) -> Optional[str]:
        return self.primary_key[0] if len(self.primary_key) == 1 else None

    def fk_for_column(self, column: str) -> Optional[ForeignKey]:
        for fk in self.foreign_keys:
            if fk.fk_column == column:
                return fk
        return None


class SchemaIntrospector:
    def __init__(self, conn: "pyodbc.Connection"):
        self.conn = conn

    def get_tables(self) -> List[str]:
        cur = self.conn.cursor()
        cur.execute(
            "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES "
            "WHERE TABLE_TYPE = 'BASE TABLE'"
        )
        return [row.TABLE_NAME for row in cur.fetchall()]

    def get_columns(self, table: str) -> Dict[str, ColumnInfo]:
        cur = self.conn.cursor()
        cur.execute(
            """
            SELECT c.COLUMN_NAME, c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH,
                   c.IS_NULLABLE, c.NUMERIC_PRECISION, c.NUMERIC_SCALE,
                   COLUMNPROPERTY(OBJECT_ID(?), c.COLUMN_NAME, 'IsIdentity') AS IS_IDENTITY
            FROM INFORMATION_SCHEMA.COLUMNS c
            WHERE c.TABLE_NAME = ?
            ORDER BY c.ORDINAL_POSITION
            """,
            table, table,
        )
        cols: Dict[str, ColumnInfo] = {}
        for row in cur.fetchall():
            cols[row.COLUMN_NAME] = ColumnInfo(
                name=row.COLUMN_NAME,
                data_type=row.DATA_TYPE,
                max_length=row.CHARACTER_MAXIMUM_LENGTH,
                is_nullable=(row.IS_NULLABLE == "YES"),
                is_identity=bool(row.IS_IDENTITY),
                numeric_precision=row.NUMERIC_PRECISION,
                numeric_scale=row.NUMERIC_SCALE,
            )
        return cols

    def get_primary_key(self, table: str) -> List[str]:
        cur = self.conn.cursor()
        cur.execute(
            """
            SELECT kcu.COLUMN_NAME
            FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
            JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
              ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
             AND tc.TABLE_NAME = kcu.TABLE_NAME
            WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY' AND tc.TABLE_NAME = ?
            ORDER BY kcu.ORDINAL_POSITION
            """,
            table,
        )
        return [row.COLUMN_NAME for row in cur.fetchall()]

    def get_foreign_keys(self) -> List[ForeignKey]:
        cur = self.conn.cursor()
        cur.execute(
            """
            SELECT
                fk_tab.name AS fk_table, fk_col.name AS fk_column,
                pk_tab.name AS ref_table, pk_col.name AS ref_column
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
            JOIN sys.tables fk_tab ON fkc.parent_object_id = fk_tab.object_id
            JOIN sys.columns fk_col ON fkc.parent_object_id = fk_col.object_id
                                    AND fkc.parent_column_id = fk_col.column_id
            JOIN sys.tables pk_tab ON fkc.referenced_object_id = pk_tab.object_id
            JOIN sys.columns pk_col ON fkc.referenced_object_id = pk_col.object_id
                                    AND fkc.referenced_column_id = pk_col.column_id
            """
        )
        return [
            ForeignKey(row.fk_table, row.fk_column, row.ref_table, row.ref_column)
            for row in cur.fetchall()
        ]

    def build_schema(self) -> Dict[str, TableInfo]:
        tables = {name: TableInfo(name=name) for name in self.get_tables()}
        for name, info in tables.items():
            info.columns = self.get_columns(name)
            info.primary_key = self.get_primary_key(name)
        for fk in self.get_foreign_keys():
            if fk.fk_table in tables:
                tables[fk.fk_table].foreign_keys.append(fk)
        return tables


def topological_order(tables: Dict[str, TableInfo]) -> List[str]:
    """Kahn's algorithm. A table only becomes eligible once every OTHER
    table it references via FK has already been placed in the order.
    Self-referencing FKs (e.g. an EmployeeId -> ApplicationUser manager
    column) are ignored for ordering purposes. Any leftover cycle is
    appended deterministically with a logged warning by the caller."""
    in_degree = {name: 0 for name in tables}
    children: Dict[str, List[str]] = {name: [] for name in tables}

    for name, info in tables.items():
        parents = {
            fk.ref_table for fk in info.foreign_keys
            if fk.ref_table != name and fk.ref_table in tables
        }
        in_degree[name] = len(parents)
        for parent in parents:
            children[parent].append(name)

    queue = sorted([n for n, d in in_degree.items() if d == 0])
    order: List[str] = []
    while queue:
        queue.sort()
        node = queue.pop(0)
        order.append(node)
        for child in children[node]:
            in_degree[child] -= 1
            if in_degree[child] == 0:
                queue.append(child)

    remaining = [n for n in tables if n not in order]
    return order + sorted(remaining)
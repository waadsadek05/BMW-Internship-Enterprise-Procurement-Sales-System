"""
state.py
--------
Mutable state threaded through the whole generation pipeline:

- PK pools per table       -> so FK columns reference REAL inserted parents
                               (never a random int that may not exist)
- monotonic date cursors    -> guarantee sequential, ascending dates per table
- sequence counters         -> e.g. InvoiceNumber, PO number generation
- a small scratch dict      -> lets one table's generator stash info the
                               next table's generator needs (e.g. "which
                               PurchaseOrders are Completed and still need
                               a VendorInvoice")
"""
import random
from dataclasses import dataclass, field
from datetime import datetime, timedelta
from typing import Dict, List


@dataclass
class DateCursor:
    current: datetime

    def advance(self, min_hours: float = 1, max_hours: float = 36) -> datetime:
        """Move strictly forward in time and return the new timestamp.
        This is what guarantees requirement #4: newer rows in a table
        always carry dates >= older rows in that same table."""
        # forward-only: the random float already gives realistic hour/minute
        # variety; adding seconds (never replacing minute) cannot go backwards.
        delta = timedelta(hours=random.uniform(min_hours, max_hours), seconds=random.randint(0, 59))
        self.current = (self.current + delta).replace(microsecond=0)
        return self.current

    def advance_from(self, base: datetime, min_hours: float = 1, max_hours: float = 72) -> datetime:
        """For dependent dates (e.g. VendorInvoice.Date must be >= the
        PurchaseOrder.Date it was raised from). Also updates this cursor
        so this table's OWN sequence stays monotonic."""
        candidate = base + timedelta(hours=random.uniform(min_hours, max_hours))
        if candidate < self.current:
            candidate = self.current + timedelta(hours=random.uniform(min_hours, max_hours))
        self.current = candidate
        return self.current


class AgentState:
    def __init__(self, data_year: int):
        self.data_year = data_year
        self.pk_pools: Dict[str, List[int]] = {}
        self.date_cursors: Dict[str, DateCursor] = {}
        self.sequence_counters: Dict[str, int] = {}
        self.extra: Dict[str, dict] = {}

    # -- PK / FK bookkeeping -------------------------------------------------
    def register_pk(self, table: str, pk_value: int) -> None:
        self.pk_pools.setdefault(table, []).append(pk_value)

    def get_pk_pool(self, table: str) -> List[int]:
        return self.pk_pools.get(table, [])

    def random_fk(self, table: str) -> int:
        pool = self.get_pk_pool(table)
        if not pool:
            raise ValueError(
                f"Cannot generate a FK into '{table}': no rows have been "
                f"inserted there yet. Check the dependency/topological order."
            )
        return random.choice(pool)

    # -- dates ----------------------------------------------------------------
    def date_cursor(self, table: str, start: datetime) -> DateCursor:
        if table not in self.date_cursors:
            self.date_cursors[table] = DateCursor(current=start)
        return self.date_cursors[table]

    # -- misc sequences (invoice numbers, PO numbers, ...) --------------------
    def next_sequence(self, key: str) -> int:
        self.sequence_counters[key] = self.sequence_counters.get(key, 0) + 1
        return self.sequence_counters[key]
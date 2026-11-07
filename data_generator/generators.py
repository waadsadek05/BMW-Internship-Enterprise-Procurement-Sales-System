"""
generators.py
-------------
The generation brain of the agent.

Design:
  1. A GENERIC column resolver handles the common cases for every table
     by pattern-matching the column NAME (status / date / location /
     contact / reason / invoice number / FK / amount / quantity / flag),
     so the agent doesn't need hardcoded knowledge of every column in
     the real schema.
  2. A small number of TABLE-SPECIFIC generators override the generic
     path only where real business logic is required: order lifecycle
     (Pending -> Completed/Cancelled), VendorInvoice creation triggered
     by an "approved" PurchaseOrder, ApplicationUser <-> Branch/Role
     linkage, BranchInventory pairing, and item-line tables whose
     parent header totals must be rolled up afterwards.

All identity columns are NEVER assigned a value here — SQL Server owns
those. Non-identity primary keys (if any) are assigned sequentially by
AgentState, never left to string placeholders.
"""
import random
import re
import logging
from datetime import datetime, timedelta
from typing import Dict, List, Optional

from faker import Faker

import domain_rules as dr
from config import (
    DATA_YEAR, DEFAULT_ORDER_STATUSES, ORDER_CREATION_STATUS,
    VENDOR_INVOICE_STATUSES, VENDOR_INVOICE_CREATION_STATUS,
    USER_ROLE_CODES, ROW_COUNT_PLAN, TARGET_BRANCH_COUNT, DATA_START, DATA_END,
)
from schema_introspector import TableInfo, ColumnInfo
from state import AgentState

fake = Faker()
logger = logging.getLogger("agent.generators")

_DATE_TYPES = ("date", "datetime", "datetime2", "smalldatetime")
_WAREHOUSE_LABELS = ["Main Warehouse", "Spare Parts Warehouse", "Overflow Warehouse"]

# Role mix used for every 20 users of a branch: 1 Admin, 4 PO_Creator,
# 3 PO_Approver, 8 Sales_Creator, 4 Sales_Approver (indexes into
# USER_ROLE_CODES). Each user gets exactly ONE role.
_R = {code: i for i, code in enumerate(USER_ROLE_CODES)}
_USER_ROLE_PATTERN = (
    [_R["Admin"]] + [_R["PO_Creator"]] * 4 + [_R["PO_Approver"]] * 3
    + [_R["Sales_Creator"]] * 8 + [_R["Sales_Approver"]] * 4
)
_SALES_CREATOR_IDX = _R["Sales_Creator"]

# ---------------------------------------------------------------------------
# Table-name canonicalization: real schemas vary between singular/plural,
# so we normalize and match against known aliases rather than assuming an
# exact spelling.
# ---------------------------------------------------------------------------

_ALIASES = {
    "role": ["role", "roles", "approle", "approles", "aspnetroles"],
    "branch": ["branch", "branches"],
    "warehouse": ["warehouse", "warehouses"],
    "category": ["category", "categories"],
    "vendor": ["vendor", "vendors"],
    "customer": ["customer", "customers"],
    "product": ["product", "products"],
    "applicationuser": ["applicationuser", "applicationusers", "appapplicationuser", "appapplicationusers", "appuser", "appusers", "aspnetusers", "user", "users"],
    "userrole": ["userrole", "userroles", "appuserrole", "appuserroles", "aspnetuserroles"],
    "purchaseorder": ["purchaseorder", "purchaseorders"],
    "purchaseorderitem": ["purchaseorderitem", "purchaseorderitems"],
    "vendorinvoice": ["vendorinvoice", "vendorinvoices"],
    "salesorder": ["salesorder", "salesorders"],
    "salesorderitem": ["salesorderitem", "salesorderitems"],
    "salesinvoice": ["salesinvoice", "salesinvoices"],
    "invoiceitem": ["invoiceitem", "invoiceitems"],
    "branchinventory": ["branchinventory", "branchinventories"],
    "creditnote": ["creditnote", "creditnotes"],
    "creditnoteitem": ["creditnoteitem", "creditnoteitems"],
}
_ALIAS_LOOKUP = {alias: key for key, aliases in _ALIASES.items() for alias in aliases}


def normalize(name: str) -> str:
    return re.sub(r"[^a-z0-9]", "", name.lower())


def canonicalize(table_name: str) -> str:
    return _ALIAS_LOOKUP.get(normalize(table_name), normalize(table_name))


def _looks_like(col_name: str, *needles: str) -> bool:
    n = col_name.lower()
    return any(needle in n for needle in needles)


def _warn_fallback(table_info: TableInfo, expected: str, fallback_count) -> None:
    actual_fks = [f"{fk.fk_column} -> {fk.ref_table}" for fk in table_info.foreign_keys]
    logger.warning(
        "%s: expected an FK to %s but found %s -- falling back to %s generic "
        "row(s). If this table SHOULD be linked, the real column/table name "
        "doesn't match what this generator looked for; check the FK list above "
        "against domain_rules/generators.py's canonicalize() aliases.",
        table_info.name, expected, actual_fks or "no FKs at all on this table", fallback_count,
    )


class DataGenerator:
    def __init__(self, schema: Dict[str, TableInfo], state: AgentState, llm):
        self.schema = schema
        self.state = state
        self.llm = llm

    # -----------------------------------------------------------------
    # Public entry point
    # -----------------------------------------------------------------
    def generate_for_table(self, table_info: TableInfo) -> List[Dict]:
        canonical = canonicalize(table_info.name)
        handler = getattr(self, f"_gen_{canonical}", None)
        if handler:
            return handler(table_info)
        return self._gen_generic(table_info, self._row_count(canonical, default=25))

    # -----------------------------------------------------------------
    # Row-count planning
    # -----------------------------------------------------------------
    def _row_count(self, canonical: str, default: int) -> int:
        return ROW_COUNT_PLAN.get(canonical, default)

    # -----------------------------------------------------------------
    # Lexical helpers (LLM-assisted, Faker-backed fallback -> never
    # hallucinated garbage, never a placeholder string)
    # -----------------------------------------------------------------
    def _lexical_batch(self, kind: str, count: int, context: str = "") -> List[str]:
        system = (
            "You generate realistic, professional business names for an "
            "automotive spare-parts procurement & sales system. Output a "
            f"JSON array of exactly {count} short strings, no numbering, "
            "no explanations, each unique."
        )
        user = f"Generate {count} realistic {kind}. {context}".strip()
        result = self.llm.generate_json_list(system, user, count) if self.llm else None
        names: List[str] = []
        if result:
            for item in result:
                val = item if isinstance(item, str) else item.get("name") or item.get("value")
                if val and not dr.is_placeholder(val):
                    names.append(str(val).strip())
        # top up / fully fall back with Faker so we always return `count` items
        while len(names) < count:
            if kind.startswith("vendor"):
                _, last = dr.random_person()
                names.append(f"{last} {random.choice(dr.EG_COMPANY_SUFFIXES)}")
            elif kind.startswith("customer"):
                first, last = dr.random_person()
                names.append(f"{first} {last}" if random.random() < 0.75 else f"{last} {random.choice(dr.EG_COMPANY_SUFFIXES)}")
            elif kind.startswith("product"):
                names.append(f"{random.choice(['OEM', 'Premium', 'Standard'])} {fake.word().capitalize()} Assembly")
            else:
                names.append(fake.word().capitalize())
        # de-duplicate while preserving order
        seen, unique = set(), []
        for n in names:
            if n not in seen:
                seen.add(n)
                unique.append(n)
        while len(unique) < count:
            unique.append(f"{unique[0]} #{len(unique)+1}")
        return unique[:count]

    # -----------------------------------------------------------------
    # Generic per-column resolver — used by every table unless a
    # table-specific generator below overrides it.
    # -----------------------------------------------------------------
    def _resolve_column(self, col: ColumnInfo, table_info: TableInfo, row_ctx: dict, row_index: int):
        name = col.name

        # 1. Foreign keys -> always point at a REAL, already-inserted parent row
        fk = table_info.fk_for_column(name)
        if fk and fk.ref_table != table_info.name:
            return self.state.random_fk(fk.ref_table)

        # 2. Status-like columns
        if _looks_like(name, "status"):
            if _looks_like(table_info.name, "order"):
                return ORDER_CREATION_STATUS
            if _looks_like(table_info.name, "invoice"):
                return VENDOR_INVOICE_CREATION_STATUS
            return "Active"

        # 3. Domain-mandated text columns (zero hallucination)
        if _looks_like(name, "contact"):
            return dr.random_vendor_contact(row_ctx.get("Name", "vendor"))
        if _looks_like(name, "location", "address"):
            return dr.random_egypt_location()
        if _looks_like(name, "reason"):
            return dr.random_credit_note_reason()
        if _looks_like(name, "invoicenumber"):
            seq = self.state.next_sequence(f"invnum::{table_info.name}")
            return dr.format_invoice_number(seq, DATA_YEAR)
        if _looks_like(name, "email"):
            # Match whatever Name-like column was already resolved on THIS
            # row (e.g. "FullName", "ContactName", "CompanyName") so the
            # email actually belongs to the same person/entity -- looking
            # for a literal "Name" key here was the bug that let email
            # silently drift to a random, unrelated name.
            base = next((v for k, v in row_ctx.items() if _looks_like(k, "name") and isinstance(v, str) and v), None)
            base = base or fake.first_name()
            slug = re.sub(r"[^a-z0-9]+", ".", str(base).lower()).strip(".")
            return f"{slug or 'user'}@{fake.free_email_domain()}"
        if _looks_like(name, "phone", "mobile"):
            return dr.random_egypt_mobile()
        if _looks_like(name, "password", "pwd"):
            return dr.random_password()

        # 4. Dates / timestamps -> spread evenly over [DATA_START, DATA_END], strictly
        #    ascending per table (newer row => later date), never in the future.
        if col.data_type in _DATE_TYPES or _looks_like(name, "date"):
            n_rows = max(self._row_count(canonicalize(table_info.name), 200), 1)
            window_hours = max((DATA_END - timedelta(days=2) - DATA_START).total_seconds() / 3600.0, 1.0)
            mean_step = window_hours / n_rows
            cursor = self.state.date_cursor(table_info.name, DATA_START)
            return min(cursor.advance(0.2 * mean_step, 1.8 * mean_step), DATA_END)

        # 5. Money / quantity heuristics
        if _looks_like(name, "price", "cost", "amount", "total", "subtotal", "grandtotal"):
            return round(random.uniform(50, 15000), 2)
        if _looks_like(name, "quantity", "qty", "stock"):
            return random.randint(1, 100)

        # 6. Names / descriptions -> lexical batch cached per table
        if _looks_like(name, "name") and not col.is_identity:
            cache_key = f"lexnames::{table_info.name}"
            cache = self.state.extra.setdefault(cache_key, {}).get("pool")
            if cache is None:
                kind = "vendor company names" if _looks_like(table_info.name, "vendor") else (
                    "customer names" if _looks_like(table_info.name, "customer") else (
                        "automotive spare part product names" if _looks_like(table_info.name, "product")
                        else "short category labels"
                    )
                )
                pool = self._lexical_batch(kind, max(self._row_count(canonicalize(table_info.name), 20), 20))
                self.state.extra[cache_key] = {"pool": pool, "i": 0}
                cache = pool
            slot = self.state.extra[cache_key]
            value = slot["pool"][slot["i"] % len(slot["pool"])]
            slot["i"] += 1
            return value
        if _looks_like(name, "description", "notes", "remarks") and col.data_type in ("nvarchar", "varchar", "text", "ntext"):
            return random.choice(dr.AUTOMOTIVE_PRODUCT_CATEGORIES) + " related item"

        # 7. Booleans / bits
        if col.data_type == "bit":
            return random.random() < 0.85

        # 8. Generic type-based fallback (last resort, never a placeholder string)
        if col.data_type in ("int", "bigint", "smallint", "tinyint"):
            return random.randint(1, 100)
        if col.data_type in ("decimal", "numeric", "money", "smallmoney", "float", "real"):
            return round(random.uniform(1, 1000), 2)
        if col.data_type in ("uniqueidentifier",):
            import uuid
            return str(uuid.uuid4())
        if col.data_type in ("nvarchar", "varchar", "char", "nchar", "text", "ntext"):
            # Last-resort fallback: we don't know the column's semantics, and
            # schema_introspector doesn't expose UNIQUE constraints (only PK/FK),
            # so a plain fake.word() WILL eventually collide -- Faker's English
            # word list has only ~200 entries, and this branch can be asked for
            # hundreds of rows (e.g. Products.SKU/Code). Always fold in a
            # per-table-per-column sequence number so the value is guaranteed
            # unique regardless of whether a UNIQUE constraint is actually there.
            max_len = col.max_length if (col.max_length and col.max_length > 0) else 30
            seq = self.state.next_sequence(f"genericstr::{table_info.name}::{name}")
            word = fake.word().capitalize()
            suffix = str(seq)
            value = f"{word}-{suffix}"
            if max_len and len(value) > max_len:
                keep = max_len - len(suffix) - 1
                value = f"{word[:keep]}-{suffix}" if keep > 0 else suffix[:max_len]
            return value
        return None

    def _gen_generic(self, table_info: TableInfo, count: int) -> List[Dict]:
        rows = []
        for i in range(count):
            row = {}
            for col in table_info.columns.values():
                if col.is_identity:
                    continue
                row[col.name] = self._resolve_column(col, table_info, row, i)
            rows.append(row)
        return rows

    # -----------------------------------------------------------------
    # Table-specific overrides (business logic that a generic resolver
    # cannot safely infer)
    # -----------------------------------------------------------------
    def _gen_role(self, table_info: TableInfo) -> List[Dict]:
        rows = []
        for code in USER_ROLE_CODES:
            row = {}
            for col in table_info.columns.values():
                if col.is_identity:
                    continue
                if _looks_like(col.name, "name", "code"):
                    row[col.name] = code
                else:
                    row[col.name] = self._resolve_column(col, table_info, row, 0)
            rows.append(row)
        return rows

    def _gen_category(self, table_info: TableInfo) -> List[Dict]:
        """Category names are the fixed, real automotive-parts categories
        from domain_rules (Engine Parts, Brake System, ...) — never the
        generic word-fallback. The exact generation order is cached so
        _gen_product below can (when insertion preserves row order, which
        it does for this table's single small INSERT) look up which real
        Category.Id corresponds to which category name, and pick a
        matching BMW part type."""
        names = list(dr.AUTOMOTIVE_PRODUCT_CATEGORIES)
        self.state.extra["category_name_order"] = names
        name_col = next((c.name for c in table_info.columns.values()
                          if _looks_like(c.name, "name") and not c.is_identity), None)
        rows = []
        for i, cat_name in enumerate(names):
            row = {}
            for col in table_info.columns.values():
                if col.is_identity:
                    continue
                if name_col and col.name == name_col:
                    row[col.name] = cat_name
                else:
                    row[col.name] = self._resolve_column(col, table_info, row, i)
            rows.append(row)
        return rows

    def _gen_product(self, table_info: TableInfo) -> List[Dict]:
        """BMW spare-part product names (e.g. 'BMW 3 Series Front Brake
        Pad Set'), matched to the product's actual CategoryId whenever the
        Category.Id <-> name mapping cached by _gen_category lines up with
        the inserted PK pool; otherwise falls back to the flattened BMW
        part-type pool so names stay BMW-branded regardless.

        SKU is derived from the SAME model+part chosen for Name (e.g.
        'SER-FBPS-0007'), instead of the generic column resolver's
        unrelated 'Word-N' fallback -- so the SKU always reads as
        belonging to that specific product."""
        count = self._row_count("product", 200)
        category_fk = next((fk for fk in table_info.foreign_keys
                             if canonicalize(fk.ref_table) == "category"), None)
        category_name_by_id: Dict[int, str] = {}
        if category_fk:
            cat_pool = self.state.get_pk_pool(category_fk.ref_table)
            cat_names = self.state.extra.get("category_name_order", [])
            if cat_pool and cat_names and len(cat_pool) == len(cat_names):
                category_name_by_id = dict(zip(cat_pool, cat_names))

        name_col = next((c.name for c in table_info.columns.values()
                          if _looks_like(c.name, "name") and not c.is_identity), None)
        price_col = self._find_col(table_info, "price")
        sku_col = next((c.name for c in table_info.columns.values()
                         if _looks_like(c.name, "sku", "partnumber", "itemcode", "productcode")
                         and not c.is_identity and c.name != name_col), None)
        used_names: Dict[str, int] = {}
        rows = []
        for i in range(count):
            row = {}
            if category_fk:
                row[category_fk.fk_column] = self.state.random_fk(category_fk.ref_table)

            cat_name = category_name_by_id.get(row.get(category_fk.fk_column)) if category_fk else None
            parts_pool = dr.CATEGORY_PART_TYPES.get(cat_name) or dr.GENERIC_PART_TYPES
            # pick a model/generation/part combination that hasn't been used yet
            # (retry a few times before falling back to a numeric suffix)
            for _ in range(30):
                model, generation = random.choice(dr.BMW_MODEL_VARIANTS)
                part = random.choice(parts_pool)
                pname = f"BMW {model} ({generation}) {part}"
                if pname not in used_names:
                    break
            if pname in used_names:
                used_names[pname] += 1
                pname = f"{pname} ({used_names[pname]})"
            else:
                used_names[pname] = 1

            for col in table_info.columns.values():
                if col.is_identity or col.name in row:
                    continue
                if name_col and col.name == name_col:
                    max_len = col.max_length if (col.max_length and col.max_length > 0) else 100
                    row[col.name] = pname[:max_len] if len(pname) > max_len else pname
                elif sku_col and col.name == sku_col:
                    row[col.name] = dr.sku_code(model, part, i + 1, max_len=col.max_length, chassis=generation)
                elif price_col and col.name == price_col:
                    row[col.name] = dr.random_part_price(part, model)
                else:
                    row[col.name] = self._resolve_column(col, table_info, row, i)
            rows.append(row)
        return rows

    def _gen_branch(self, table_info: TableInfo) -> List[Dict]:
        """BMW Egypt's real dealer network (see domain_rules.BMW_EGYPT_BRANCHES,
        sourced from bmw-egypt.com's dealer locator) -- not a randomly
        generated location. Generation order is cached so _gen_warehouse
        below can tie each Warehouse back to its actual owning Branch."""
        branches = dr.BMW_EGYPT_BRANCHES
        self.state.extra["branch_order"] = branches
        name_col = next((c.name for c in table_info.columns.values()
                          if _looks_like(c.name, "name") and not c.is_identity), None)
        loc_col = next((c.name for c in table_info.columns.values()
                         if _looks_like(c.name, "location", "address")), None)
        rows = []
        for i, b in enumerate(branches):
            row = {}
            for col in table_info.columns.values():
                if col.is_identity:
                    continue
                if name_col and col.name == name_col:
                    row[col.name] = b["name"]
                elif loc_col and col.name == loc_col:
                    row[col.name] = b["location"]
                else:
                    row[col.name] = self._resolve_column(col, table_info, row, i)
            rows.append(row)
        return rows

    # -----------------------------------------------------------------
    # Small helpers shared by the table-specific generators below
    # -----------------------------------------------------------------
    def _fk_to(self, table_info: TableInfo, canonical: str):
        return next((fk for fk in table_info.foreign_keys if canonicalize(fk.ref_table) == canonical), None)

    def _find_col(self, table_info: TableInfo, *needles: str) -> Optional[str]:
        return next((c.name for c in table_info.columns.values()
                     if not c.is_identity and _looks_like(c.name, *needles)), None)

    def _date_col(self, table_info: TableInfo) -> Optional[str]:
        return next((c.name for c in table_info.columns.values()
                     if c.data_type in _DATE_TYPES or _looks_like(c.name, "date")), None)

    def _find_table_info(self, canonical_name: str) -> Optional[TableInfo]:
        for t in self.schema.values():
            if canonicalize(t.name) == canonical_name:
                return t
        return None

    # -- prices --------------------------------------------------------------
    # Products.UnitPrice is the SELLING price. It is read back from the DB
    # right after Products are inserted (see after_insert) so every sales
    # line uses the product's real list price. The PURCHASE cost is always
    # a fixed fraction (68-86%) of that price, so we always buy below the
    # price we sell at -- never two unrelated random numbers.
    def after_insert(self, table_info: TableInfo, conn) -> None:
        if canonicalize(table_info.name) != "product":
            return
        price_col = next((c.name for c in table_info.columns.values() if _looks_like(c.name, "price")), None)
        pk = table_info.single_pk
        if not price_col or not pk:
            return
        cur = conn.cursor()
        cur.execute(f"SELECT [{pk}], [{price_col}] FROM [{table_info.name}]")
        self.state.extra["product_price"] = {int(r[0]): float(r[1]) for r in cur.fetchall()}

    def _price_of(self, product_id) -> float:
        prices = self.state.extra.get("product_price", {})
        if product_id in prices:
            return prices[product_id]
        fallback = self.state.extra.setdefault("product_price_fallback", {})
        if product_id not in fallback:
            fallback[product_id] = round(random.uniform(500, 15000), 2)
        return fallback[product_id]

    def _cost_of(self, product_id) -> float:
        costs = self.state.extra.setdefault("product_cost", {})
        if product_id not in costs:
            costs[product_id] = round(self._price_of(product_id) * random.uniform(0.68, 0.86), 2)
        return costs[product_id]

    # -- order lines ---------------------------------------------------------
    @staticmethod
    def _sales_qty(unit_price: float) -> int:
        """Customers buy 1-2 of an expensive part, a handful of cheap ones."""
        if unit_price > 20000:
            return random.choices([1, 2], weights=[85, 15])[0]
        if unit_price > 5000:
            return random.choices([1, 2, 3, 4], weights=[60, 25, 10, 5])[0]
        return random.choices([1, 2, 3, 4, 5, 6, 8, 10], weights=[35, 25, 13, 9, 6, 5, 4, 3])[0]

    @staticmethod
    def _purchase_qty(unit_price: float) -> int:
        """Procurement buys in batches, but a branch doesn't stock 40 turbochargers."""
        if unit_price > 50000:
            return random.randint(1, 4)
        if unit_price > 20000:
            return random.randint(2, 10)
        if unit_price > 5000:
            return random.randint(5, 25)
        return random.randint(10, 60)

    @staticmethod
    def _line_total(item: Dict) -> float:
        qty = next((v for k, v in item.items() if _looks_like(k, "quantity", "qty")), 0)
        price = next((v for k, v in item.items() if _looks_like(k, "price", "cost")), 0)
        return qty * price

    def _items_total(self, items: List[Dict]) -> float:
        return round(sum(self._line_total(i) for i in items), 2)

    def _build_item_row(self, item_table: TableInfo, parent_fk, product_fk, product_id, quantity, unit_price) -> Dict:
        row: Dict = {}
        if product_fk:
            row[product_fk.fk_column] = product_id
        for col in item_table.columns.values():
            if col.is_identity or col.name in row or (parent_fk and col.name == parent_fk.fk_column):
                continue
            if _looks_like(col.name, "quantity", "qty"):
                row[col.name] = quantity
            elif _looks_like(col.name, "price", "cost"):
                row[col.name] = unit_price
            else:
                row[col.name] = self._resolve_column(col, item_table, row, 0)
        return row

    # -- dates ---------------------------------------------------------------
    @staticmethod
    def _dependent_date(base: Optional[datetime], prev: Optional[datetime], min_hours: float, max_hours: float) -> datetime:
        """A date that is ALWAYS >= the document it came from (`base`),
        never earlier than the previous row in the same table (`prev`, so
        the table's own dates only ever move forward) and never in the
        future."""
        d = (base or DATA_START) + timedelta(hours=random.uniform(min_hours, max_hours))
        if prev is not None and d < prev:
            d = prev + timedelta(minutes=random.randint(1, 45))
        return min(d, DATA_END).replace(microsecond=0)

    @staticmethod
    def _status_weights(order_date: Optional[datetime]) -> List[float]:
        """[Pending, Completed, Cancelled]. Old orders are almost never
        still Pending; only recent ones are."""
        if order_date is None:
            return [0.30, 0.55, 0.15]
        age_days = (DATA_END - order_date).days
        if age_days <= 14:
            return [0.55, 0.35, 0.10]
        if age_days <= 60:
            return [0.12, 0.72, 0.16]
        return [0.02, 0.85, 0.13]

    # -----------------------------------------------------------------
    # Warehouses / users / roles
    # -----------------------------------------------------------------
    def _gen_warehouse(self, table_info: TableInfo) -> List[Dict]:
        """Every branch gets 1-3 warehouses (a branch can have several),
        each named after and located at that SAME real branch (e.g.
        'Kattameya Main Warehouse'). Branch <-> PK matching is positional
        via the order cached in _gen_branch (holds for that table's single
        small INSERT). The owning branch of every generated warehouse is
        cached so BranchInventory can put stock into the right branch's
        warehouses."""
        branch_fk = self._fk_to(table_info, "branch")
        branch_order = self.state.extra.get("branch_order", [])
        branch_pool = self.state.get_pk_pool(branch_fk.ref_table) if branch_fk else []
        if not branch_pool:
            _warn_fallback(table_info, "Branch", TARGET_BRANCH_COUNT)
            return self._gen_generic(table_info, TARGET_BRANCH_COUNT)
        branch_by_id: Dict[int, dict] = {}
        if branch_order and len(branch_pool) == len(branch_order):
            branch_by_id = dict(zip(branch_pool, branch_order))

        name_col = self._find_col(table_info, "name")
        loc_col = self._find_col(table_info, "location", "address")
        rows: List[Dict] = []
        owner_branch_ids: List[int] = []
        for branch_id in branch_pool:
            b = branch_by_id.get(branch_id)
            short = re.sub(r"^BMW (Center|Showroom)\s+", "", b["name"]) if b else f"Branch {branch_id}"
            base_loc = b["location"] if b else dr.random_egypt_location()
            n_warehouses = random.choices([1, 2, 3], weights=[0.50, 0.35, 0.15])[0]
            for j in range(n_warehouses):
                row: Dict = {branch_fk.fk_column: branch_id}
                for col in table_info.columns.values():
                    if col.is_identity or col.name in row:
                        continue
                    if col.name == name_col:
                        row[col.name] = f"{short} {_WAREHOUSE_LABELS[j]}"
                    elif col.name == loc_col:
                        row[col.name] = base_loc if j == 0 else f"{base_loc} (Annex {j})"
                    else:
                        row[col.name] = self._resolve_column(col, table_info, row, j)
                rows.append(row)
                owner_branch_ids.append(branch_id)
        self.state.extra["warehouse_branch_ids"] = owner_branch_ids
        return rows

    def _gen_applicationuser(self, table_info: TableInfo) -> List[Dict]:
        """FullName / Email / Username must all describe the SAME person,
        and Password must look like a real password -- not the generic
        word-fallback. Each user gets one (first, last) identity generated
        once, and Email/Username are both derived from it (in different,
        realistic formats).

        Users are spread evenly over the branches, and every branch gets a
        realistic mix of roles (see _USER_ROLE_PATTERN). Each user's branch
        and role are cached in `user_meta` so SalesOrders can later be
        created by a Sales_Creator user AT THAT USER'S OWN BRANCH, and
        UserRoles can assign exactly the role decided here."""
        count = self._row_count("applicationuser", 80)
        branch_fk = self._fk_to(table_info, "branch")
        role_fk = self._fk_to(table_info, "role")
        branch_pool = self.state.get_pk_pool(branch_fk.ref_table) if branch_fk else self.state.get_pk_pool("Branch")
        n_branches = max(len(branch_pool), 1)

        name_col = next((c.name for c in table_info.columns.values()
                          if _looks_like(c.name, "name") and "user" not in c.name.lower()), None)
        email_col = next((c.name for c in table_info.columns.values() if _looks_like(c.name, "email")), None)
        username_col = next((c.name for c in table_info.columns.values() if _looks_like(c.name, "username")), None)
        password_col = next((c.name for c in table_info.columns.values() if _looks_like(c.name, "password")), None)

        used_emails: set = set()
        rows: List[Dict] = []
        user_meta: List[Dict] = []
        for i in range(count):
            first, last = dr.random_person()
            full_name = f"{first} {last}"
            local = f"{first}.{last}".lower()
            username = local
            n = 1
            while f"{username}@{dr.USER_EMAIL_DOMAIN}" in used_emails:
                n += 1
                username = f"{local}{n}"
            email = f"{username}@{dr.USER_EMAIL_DOMAIN}"
            used_emails.add(email)

            branch_id = branch_pool[i % n_branches] if branch_pool else None
            role_index = _USER_ROLE_PATTERN[(i // n_branches) % len(_USER_ROLE_PATTERN)]
            user_meta.append({"branch_id": branch_id, "role_index": role_index})

            row: Dict = {}
            for col in table_info.columns.values():
                if col.is_identity:
                    continue
                if branch_fk and col.name == branch_fk.fk_column:
                    row[col.name] = branch_id if branch_id is not None else self.state.random_fk(branch_fk.ref_table)
                elif role_fk and col.name == role_fk.fk_column:
                    role_pool = self.state.get_pk_pool(role_fk.ref_table)
                    row[col.name] = role_pool[role_index % len(role_pool)] if role_pool else self.state.random_fk(role_fk.ref_table)
                elif name_col and col.name == name_col:
                    row[col.name] = full_name
                elif email_col and col.name == email_col:
                    row[col.name] = email
                elif username_col and col.name == username_col:
                    row[col.name] = username
                elif password_col and col.name == password_col:
                    row[col.name] = dr.random_password()
                else:
                    row[col.name] = self._resolve_column(col, table_info, row, i)
            rows.append(row)
        self.state.extra["user_meta"] = user_meta
        return rows

    def _gen_userrole(self, table_info: TableInfo) -> List[Dict]:
        """Exactly ONE role per user (so an Admin never also carries a
        second role -- Admin already does everything), using the role
        decided for that user in _gen_applicationuser."""
        user_fk = self._fk_to(table_info, "applicationuser")
        role_fk = self._fk_to(table_info, "role")
        if not user_fk or not role_fk:
            _warn_fallback(table_info, "ApplicationUser and Role", 20)
            return self._gen_generic(table_info, 20)
        users = self.state.get_pk_pool(user_fk.ref_table)
        roles = self.state.get_pk_pool(role_fk.ref_table)
        meta = self.state.extra.get("user_meta", [])
        rows = []
        for i, user_id in enumerate(users):
            role_index = meta[i]["role_index"] if i < len(meta) else i
            row = {user_fk.fk_column: user_id, role_fk.fk_column: roles[role_index % len(roles)]}
            for col in table_info.columns.values():
                if col.is_identity or col.name in row:
                    continue
                row[col.name] = self._resolve_column(col, table_info, row, i)
            rows.append(row)
        return rows

    # -----------------------------------------------------------------
    # Purchase side: PurchaseOrder -> PurchaseOrderItem -> VendorInvoice
    #                -> BranchInventory (stock received)
    # -----------------------------------------------------------------
    def _order_with_lifecycle(self, table_info: TableInfo, count: int) -> List[Dict]:
        """Shared logic for PurchaseOrder / SalesOrder: every row is
        created as 'Pending' and then its lifecycle is simulated forward
        (approve -> Completed, or Cancelled), while STILL guaranteeing the
        creation status is always exactly 'Pending' per the spec. The
        final status depends on how old the order is: old orders are
        settled, only recent ones are still Pending."""
        date_col = self._date_col(table_info)
        rows = []
        for i in range(count):
            row = {}
            for col in table_info.columns.values():
                if col.is_identity:
                    continue
                if _looks_like(col.name, "status"):
                    row[col.name] = ORDER_CREATION_STATUS  # always starts here
                    continue
                row[col.name] = self._resolve_column(col, table_info, row, i)
            order_date = row.get(date_col) if date_col else None
            final_status = random.choices(DEFAULT_ORDER_STATUSES, weights=self._status_weights(order_date), k=1)[0]
            for col in table_info.columns.values():
                if _looks_like(col.name, "status"):
                    row[col.name] = final_status
            row["__final_status__"] = final_status
            rows.append(row)
        return rows


    def _assign_order_users(self, rows: List[Dict], table_info: TableInfo, creator_idx: int, approver_idx: int) -> None:
        """Creator FK -> a user holding the creator role; approver FK -> a user
        holding the approver role; BOTH at the same branch as the order."""
        user_fks = [fk for fk in table_info.foreign_keys if canonicalize(fk.ref_table) == "applicationuser"]
        branch_fk = self._fk_to(table_info, "branch")
        if not user_fks:
            return
        ref = user_fks[0].ref_table
        user_ids = self.state.get_pk_pool(ref)
        meta = self.state.extra.get("user_meta", [])
        if len(user_ids) != len(meta):
            return
        by_role: Dict[int, List[tuple]] = {}
        for uid, m in zip(user_ids, meta):
            by_role.setdefault(m["role_index"], []).append((uid, m["branch_id"]))
        creators = by_role.get(creator_idx)
        if not creators:
            return
        for row in rows:
            uid, branch_id = random.choice(creators)
            if branch_fk and branch_id is not None:
                row[branch_fk.fk_column] = branch_id
            for fk in user_fks:
                col = table_info.columns[fk.fk_column]
                if "approv" in fk.fk_column.lower():
                    same_branch = [u for u, b in by_role.get(approver_idx, []) if b == branch_id]
                    pool = same_branch or [u for u, _ in by_role.get(approver_idx, [])]
                    if not pool:
                        continue
                    if col.is_nullable and row.get("__final_status__") == "Pending":
                        row[fk.fk_column] = None
                    else:
                        row[fk.fk_column] = random.choice(pool)
                else:
                    row[fk.fk_column] = uid

    # -- header totals = sum of the order's lines (applied as UPDATEs after items exist)
    def header_total_updates(self) -> List[tuple]:
        """[(table_info, total_col, pk_col, [(total, pk), ...]), ...] for PO and SO so
        the header total always equals the sum of its own item lines."""
        out = []
        for canonical, items_key, build in (
            ("purchaseorder", "purchaseorder_items", self._purchaseorder_items_map),
            ("salesorder", "salesorder_items", self._salesorder_items_map),
        ):
            ti = self._find_table_info(canonical)
            if ti is None or not ti.single_pk:
                continue
            total_col = self._find_col(ti, "total", "amount")
            if not total_col:
                continue
            items_map = build()
            pairs = [(self._items_total(lines), pk) for pk, lines in items_map.items() if lines]
            out.append((ti, total_col, ti.single_pk, pairs))
        return out

    def _gen_purchaseorder(self, table_info: TableInfo) -> List[Dict]:
        rows = self._order_with_lifecycle(table_info, self._row_count("purchaseorder", 120))
        self._assign_order_users(rows, table_info, _R["PO_Creator"], _R["PO_Approver"])
        branch_fk = self._fk_to(table_info, "branch")
        date_col = self._date_col(table_info)
        # positional (insertion-order) facts about every PO, used by
        # VendorInvoice (only Completed POs get invoiced) and by
        # BranchInventory (only Completed POs put stock on the shelf).
        self.state.extra["purchaseorder_meta"] = [
            {
                "status": r["__final_status__"],
                "date": r.get(date_col) if date_col else None,
                "branch_id": r.get(branch_fk.fk_column) if branch_fk else None,
            }
            for r in rows
        ]
        return [{k: v for k, v in r.items() if not k.startswith("__")} for r in rows]

    def _purchaseorder_items_map(self) -> Dict[int, List[Dict]]:
        """Which products / quantities / unit costs belong to which
        PurchaseOrder. Computed ONCE (lazily) and shared by
        PurchaseOrderItem, VendorInvoice and BranchInventory so all three
        always agree on exactly what was bought."""
        cached = self.state.extra.get("purchaseorder_items")
        if cached is not None:
            return cached
        items_map: Dict[int, List[Dict]] = {}
        poi = self._find_table_info("purchaseorderitem")
        if poi is not None:
            parent_fk = self._fk_to(poi, "purchaseorder")
            product_fk = self._fk_to(poi, "product")
            po_pool = self.state.get_pk_pool(parent_fk.ref_table) if parent_fk else []
            products = self.state.get_pk_pool(product_fk.ref_table) if product_fk else []
            for po_id in po_pool:
                chosen = random.sample(products, min(random.randint(1, 5), len(products))) if products else []
                lines = []
                for product_id in chosen:
                    unit_cost = round(self._cost_of(product_id) * random.uniform(0.98, 1.02), 2)
                    lines.append(self._build_item_row(poi, parent_fk, product_fk, product_id,
                                                      self._purchase_qty(self._price_of(product_id)), unit_cost))
                items_map[po_id] = lines
        self.state.extra["purchaseorder_items"] = items_map
        return items_map

    def _gen_purchaseorderitem(self, table_info: TableInfo) -> List[Dict]:
        parent_fk = self._fk_to(table_info, "purchaseorder")
        if not parent_fk:
            return self._item_lines_for(table_info, "purchaseorder")
        rows = []
        for po_id, lines in self._purchaseorder_items_map().items():
            for item in lines:
                row = {parent_fk.fk_column: po_id}
                row.update(item)
                rows.append(row)
        return rows

    def _gen_vendorinvoice(self, table_info: TableInfo) -> List[Dict]:
        """One VendorInvoice per Completed PurchaseOrder (and ONLY those),
        dated after that PO, for exactly the total of that PO's lines
        (quantity x unit cost). Every invoice starts 'Pending'; older ones
        have mostly been 'Paid' by now."""
        po_fk = self._fk_to(table_info, "purchaseorder")
        if not po_fk:
            _warn_fallback(table_info, "PurchaseOrder", 25)
            return self._gen_generic(table_info, 25)
        po_pool = self.state.get_pk_pool(po_fk.ref_table)
        meta = self.state.extra.get("purchaseorder_meta", [])
        items_map = self._purchaseorder_items_map()
        date_col = self._date_col(table_info)
        total_col = self._find_col(table_info, "total", "amount")
        num_col = self._find_col(table_info, "invoicenumber")
        status_col = self._find_col(table_info, "status")

        rows = []
        prev = None
        for i, po_id in enumerate(po_pool):
            m = meta[i] if i < len(meta) else None
            if m is not None and m["status"] != "Completed":
                continue
            lines = items_map.get(po_id)
            if not lines:
                continue
            d = self._dependent_date(m["date"] if m else None, prev, 24, 24 * 10)
            prev = d
            paid_probability = 0.85 if (DATA_END - d).days > 45 else 0.35
            status = "Paid" if random.random() < paid_probability else VENDOR_INVOICE_CREATION_STATUS
            row = {po_fk.fk_column: po_id}
            for col in table_info.columns.values():
                if col.is_identity or col.name in row:
                    continue
                if col.name == date_col:
                    row[col.name] = d
                elif col.name == total_col:
                    row[col.name] = self._items_total(lines)
                elif col.name == num_col:
                    seq = self.state.next_sequence(f"invnum::{table_info.name}")
                    row[col.name] = dr.format_invoice_number(seq, d.year, prefix="VINV")
                elif col.name == status_col:
                    row[col.name] = status
                else:
                    row[col.name] = self._resolve_column(col, table_info, row, i)
            rows.append(row)
        return rows

    def _gen_branchinventory(self, table_info: TableInfo) -> List[Dict]:
        """Stock on hand = everything RECEIVED through Completed purchase
        orders, per branch + product (same products, same quantities as the
        PO lines -- nothing is invented), placed in one of the warehouses
        that branch owns. (The real schema links this table to Warehouse +
        Product; each Warehouse carries its own BranchId.) Pending and
        Cancelled POs put nothing on the shelf."""
        warehouse_fk = self._fk_to(table_info, "warehouse")
        product_fk = self._fk_to(table_info, "product")
        if not warehouse_fk or not product_fk:
            _warn_fallback(table_info, "Warehouse and Product", 100)
            return self._gen_generic(table_info, 100)

        wh_pool = self.state.get_pk_pool(warehouse_fk.ref_table)
        wh_owner = self.state.extra.get("warehouse_branch_ids", [])
        po_meta = self.state.extra.get("purchaseorder_meta", [])
        po_pool = self._pool_for_canonical("purchaseorder")
        poi = self._find_table_info("purchaseorderitem")
        qty_col = self._find_col(table_info, "stock", "quantity", "qty")

        if not po_pool or not po_meta or poi is None or len(wh_pool) != len(wh_owner):
            logger.warning(
                "BranchInventory: purchase-order data (or the warehouse->branch map) isn't "
                "available yet, so stock can't be derived from received POs -- falling back "
                "to a random product sample per warehouse.")
            return self._inventory_random_fallback(table_info, warehouse_fk, product_fk, wh_pool, qty_col)

        warehouses_of_branch: Dict[int, List[int]] = {}
        for wid, bid in zip(wh_pool, wh_owner):
            warehouses_of_branch.setdefault(bid, []).append(wid)

        poi_product_col = self._fk_to(poi, "product").fk_column
        poi_qty_col = self._find_col(poi, "quantity", "qty")
        items_map = self._purchaseorder_items_map()

        received: Dict[tuple, int] = {}
        for i, po_id in enumerate(po_pool):
            m = po_meta[i] if i < len(po_meta) else None
            if not m or m["status"] != "Completed":
                continue
            for item in items_map.get(po_id, []):
                key = (m["branch_id"], item[poi_product_col])
                received[key] = received.get(key, 0) + item[poi_qty_col]

        rows = []
        for (branch_id, product_id), qty in received.items():
            candidates = warehouses_of_branch.get(branch_id)
            if not candidates:
                continue
            row = {warehouse_fk.fk_column: random.choice(candidates), product_fk.fk_column: product_id}
            for col in table_info.columns.values():
                if col.is_identity or col.name in row:
                    continue
                row[col.name] = qty if col.name == qty_col else self._resolve_column(col, table_info, row, 0)
            rows.append(row)
        return rows

    def _pool_for_canonical(self, canonical: str) -> List:
        for t in self.schema.values():
            if canonicalize(t.name) == canonical:
                return self.state.get_pk_pool(t.name)
        return []

    def _inventory_random_fallback(self, table_info, warehouse_fk, product_fk, wh_pool, qty_col) -> List[Dict]:
        products = self.state.get_pk_pool(product_fk.ref_table)
        rows = []
        for warehouse_id in wh_pool:
            for product_id in random.sample(products, min(len(products), random.randint(10, 25))):
                row = {warehouse_fk.fk_column: warehouse_id, product_fk.fk_column: product_id}
                for col in table_info.columns.values():
                    if col.is_identity or col.name in row:
                        continue
                    row[col.name] = random.randint(5, 80) if col.name == qty_col else self._resolve_column(col, table_info, row, 0)
                rows.append(row)
        return rows

    # -----------------------------------------------------------------
    # Sales side: SalesOrder -> SalesOrderItem -> SalesInvoice ->
    #             InvoiceItem -> CreditNote -> CreditNoteItem
    #
    # A pure FK-topological sort only guarantees parents before children --
    # NOT any particular order among SalesOrderItem / SalesInvoice /
    # InvoiceItem / CreditNote / CreditNoteItem. So the "which products /
    # qty / price belong to which SalesOrder" map is computed ONCE, lazily,
    # by whichever of them is processed first, and cached for the rest.
    # An order, its invoice, and any return against it therefore always
    # agree on exactly what was sold.
    # -----------------------------------------------------------------
    def _gen_salesorder(self, table_info: TableInfo) -> List[Dict]:
        """Each sales order is created by a real Sales_Creator user and
        placed at THAT user's own branch (an order can't be entered by a
        Kattameya user at the Alexandria branch)."""
        rows = self._order_with_lifecycle(table_info, self._row_count("salesorder", 200))
        user_fk = self._fk_to(table_info, "applicationuser")
        branch_fk = self._fk_to(table_info, "branch")
        date_col = self._date_col(table_info)

        self._assign_order_users(rows, table_info, _SALES_CREATOR_IDX, _R["Sales_Approver"])

        # positional (insertion-order) facts: only Completed sales orders
        # are eligible to receive a SalesInvoice, and invoices are dated
        # after their order.
        self.state.extra["salesorder_meta"] = [
            {"status": r["__final_status__"], "date": r.get(date_col) if date_col else None}
            for r in rows
        ]
        return [{k: v for k, v in r.items() if not k.startswith("__")} for r in rows]

    def _salesorder_items_map(self) -> Dict[int, List[Dict]]:
        cached = self.state.extra.get("salesorder_items")
        if cached is not None:
            return cached
        items_map: Dict[int, List[Dict]] = {}
        soi = self._find_table_info("salesorderitem")
        if soi is not None:
            parent_fk = self._fk_to(soi, "salesorder")
            product_fk = self._fk_to(soi, "product")
            so_pool = self.state.get_pk_pool(parent_fk.ref_table) if parent_fk else []
            products = self.state.get_pk_pool(product_fk.ref_table) if product_fk else []
            for so_id in so_pool:
                chosen = random.sample(products, min(random.randint(1, 5), len(products))) if products else []
                items_map[so_id] = [
                    self._build_item_row(soi, parent_fk, product_fk, product_id,
                                         self._sales_qty(self._price_of(product_id)), self._price_of(product_id))
                    for product_id in chosen
                ]
        self.state.extra["salesorder_items"] = items_map
        return items_map

    def _gen_salesorderitem(self, table_info: TableInfo) -> List[Dict]:
        parent_fk = self._fk_to(table_info, "salesorder")
        rows = []
        for so_id, line_items in self._salesorder_items_map().items():
            for item in line_items:
                row = {}
                if parent_fk:
                    row[parent_fk.fk_column] = so_id
                row.update(item)
                rows.append(row)
        return rows

    def _gen_salesinvoice(self, table_info: TableInfo) -> List[Dict]:
        """One SalesInvoice per Completed SalesOrder (that has lines),
        dated after its order, for exactly the total of that order's
        lines. It remembers which SalesOrder it came from so InvoiceItem
        and CreditNote can mirror the SAME lines."""
        so_fk = self._fk_to(table_info, "salesorder")
        if not so_fk:
            _warn_fallback(table_info, "SalesOrder", 25)
            return self._gen_generic(table_info, 25)

        items_map = self._salesorder_items_map()
        so_pool = self.state.get_pk_pool(so_fk.ref_table)
        meta = self.state.extra.get("salesorder_meta", [])
        eligible = []
        for i, so_id in enumerate(so_pool):
            m = meta[i] if i < len(meta) else None
            if items_map.get(so_id) and (m is None or m["status"] == "Completed"):
                eligible.append((so_id, m["date"] if m else None))

        date_col = self._date_col(table_info)
        total_col = self._find_col(table_info, "total", "amount")
        num_col = self._find_col(table_info, "invoicenumber")

        rows: List[Dict] = []
        source_so: Dict[int, int] = {}
        invoice_meta: List[Dict] = []
        prev = None
        for i, (so_id, order_date) in enumerate(eligible):
            d = self._dependent_date(order_date, prev, 2, 72)
            prev = d
            total = self._items_total(items_map[so_id])
            row = {so_fk.fk_column: so_id}
            for col in table_info.columns.values():
                if col.is_identity or col.name in row:
                    continue
                if col.name == date_col:
                    row[col.name] = d
                elif col.name == total_col:
                    row[col.name] = total
                elif col.name == num_col:
                    seq = self.state.next_sequence(f"invnum::{table_info.name}")
                    row[col.name] = dr.format_invoice_number(seq, d.year)
                else:
                    row[col.name] = self._resolve_column(col, table_info, row, i)
            rows.append(row)
            source_so[i] = so_id  # positional correlation with insertion order
            invoice_meta.append({"so_id": so_id, "date": d, "total": total})

        self.state.extra["salesinvoice_source_so"] = source_so
        self.state.extra["salesinvoice_meta"] = invoice_meta
        return rows

    def _gen_invoiceitem(self, table_info: TableInfo) -> List[Dict]:
        """Mirrors the exact product/quantity/price lines from the
        originating SalesOrder onto its SalesInvoice -- an invoice can
        never list an item the order didn't actually have."""
        invoice_fk = self._fk_to(table_info, "salesinvoice")
        if not invoice_fk:
            return self._item_lines_for(table_info, "salesinvoice")

        invoice_pool = self.state.get_pk_pool(invoice_fk.ref_table)
        so_map = self.state.extra.get("salesinvoice_source_so", {})
        items_map = self._salesorder_items_map()
        valid_columns = {c.name for c in table_info.columns.values()}

        rows = []
        for i, invoice_id in enumerate(invoice_pool):
            for item in items_map.get(so_map.get(i), []):
                row = {invoice_fk.fk_column: invoice_id}
                for k, v in item.items():
                    if k in valid_columns and k != invoice_fk.fk_column:
                        row[k] = v
                for col in table_info.columns.values():
                    if col.is_identity or col.name in row:
                        continue
                    row[col.name] = self._resolve_column(col, table_info, row, i)
                rows.append(row)
        return rows

    def _gen_creditnote(self, table_info: TableInfo) -> List[Dict]:
        """A CreditNote is a return against a SPECIFIC SalesInvoice. It can
        only credit products that were on that invoice (i.e. on the sales
        order behind it), in quantities no larger than what was sold, at
        the price they were sold at. RefundAmount is exactly the value of
        the credited lines, and the return is dated after the invoice."""
        invoice_fk = self._fk_to(table_info, "salesinvoice")
        inv_meta = self.state.extra.get("salesinvoice_meta", [])
        if not invoice_fk or not inv_meta:
            _warn_fallback(table_info, "SalesInvoice", 25)
            return self._gen_generic(table_info, 25)

        inv_pool = self.state.get_pk_pool(invoice_fk.ref_table)
        items_map = self._salesorder_items_map()
        eligible = [i for i, m in enumerate(inv_meta) if i < len(inv_pool) and items_map.get(m["so_id"])]
        count = min(max(5, int(self._row_count("salesorder", 200) * 0.08)), len(eligible))
        chosen = sorted(random.sample(eligible, count))   # ascending -> ReturnDate stays monotone

        date_col = self._date_col(table_info)
        total_col = self._find_col(table_info, "amount", "total")
        reason_col = self._find_col(table_info, "reason")

        rows: List[Dict] = []
        cn_items_map: Dict[int, List[Dict]] = {}
        prev = None
        for n, idx in enumerate(chosen):
            m = inv_meta[idx]
            source_items = items_map[m["so_id"]]
            returned = []
            for item in random.sample(source_items, random.randint(1, len(source_items))):
                credited = dict(item)
                qty_key = next((k for k in credited if _looks_like(k, "quantity", "qty")), None)
                if qty_key:
                    sold = credited[qty_key]
                    # half of returns are the whole line, the rest partial
                    credited[qty_key] = sold if random.random() < 0.5 else random.randint(1, sold)
                returned.append(credited)

            d = self._dependent_date(m["date"], prev, 24, 24 * 30)
            prev = d
            row = {invoice_fk.fk_column: inv_pool[idx]}
            for col in table_info.columns.values():
                if col.is_identity or col.name in row:
                    continue
                if col.name == date_col:
                    row[col.name] = d
                elif col.name == total_col:
                    row[col.name] = self._items_total(returned)
                elif col.name == reason_col:
                    row[col.name] = dr.random_credit_note_reason()
                else:
                    row[col.name] = self._resolve_column(col, table_info, row, n)
            rows.append(row)
            cn_items_map[n] = returned

        self.state.extra["creditnote_items"] = cn_items_map
        return rows

    def _gen_creditnoteitem(self, table_info: TableInfo) -> List[Dict]:
        """Mirrors ONLY the lines (and quantities) decided in
        _gen_creditnote for the sales invoice being returned against --
        never a fresh random product."""
        cn_fk = self._fk_to(table_info, "creditnote")
        if not cn_fk:
            return self._item_lines_for(table_info, "creditnote")

        cn_pool = self.state.get_pk_pool(cn_fk.ref_table)
        cn_items = self.state.extra.get("creditnote_items", {})
        valid_columns = {c.name for c in table_info.columns.values()}

        rows = []
        for i, cn_id in enumerate(cn_pool):
            for item in cn_items.get(i, []):
                row = {cn_fk.fk_column: cn_id}
                for k, v in item.items():
                    if k in valid_columns and k != cn_fk.fk_column:
                        row[k] = v
                for col in table_info.columns.values():
                    if col.is_identity or col.name in row:
                        continue
                    row[col.name] = self._resolve_column(col, table_info, row, i)
                rows.append(row)
        return rows

    # -----------------------------------------------------------------
    # Generic fallback for item tables whose parent FK we couldn't
    # identify (should not happen with the real schema)
    # -----------------------------------------------------------------
    def _item_lines_for(self, table_info: TableInfo, parent_canonical: str, product_canonical: str = "product") -> List[Dict]:
        parent_fk = self._fk_to(table_info, parent_canonical)
        product_fk = self._fk_to(table_info, product_canonical)
        if not parent_fk:
            _warn_fallback(table_info, parent_canonical, 100)
            return self._gen_generic(table_info, 100)
        parents = self.state.get_pk_pool(parent_fk.ref_table)
        products = self.state.get_pk_pool(product_fk.ref_table) if product_fk else []
        rows = []
        for parent_id in parents:
            for product_id in (random.sample(products, min(random.randint(1, 5), len(products))) if products else [None]):
                row = {parent_fk.fk_column: parent_id}
                if product_fk and product_id is not None:
                    row[product_fk.fk_column] = product_id
                for col in table_info.columns.values():
                    if col.is_identity or col.name in row:
                        continue
                    row[col.name] = self._resolve_column(col, table_info, row, 0)
                rows.append(row)
        return rows
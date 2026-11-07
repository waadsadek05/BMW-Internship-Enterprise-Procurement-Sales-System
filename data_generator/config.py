"""
config.py
---------
Central configuration for the Procurement & Sales Test-Data Agent.

Loads connection / LLM settings from environment variables (.env) and
defines the domain constants the agent must NEVER violate. These are
plain Python constants (not LLM prompts) precisely so they cannot be
hallucinated away — the generation and validation layers both import
from here.
"""
import os
from dataclasses import dataclass
from datetime import datetime
from typing import List

try:
    from dotenv import load_dotenv
    load_dotenv()
except ImportError:
    # python-dotenv is optional; real env vars still work without it.
    pass


def _env_bool(name: str, default: bool) -> bool:
    val = os.getenv(name)
    if val is None:
        return default
    return val.strip().lower() in ("1", "true", "yes", "on")


@dataclass(frozen=True)
class DBConfig:
    server: str = os.getenv("DB_SERVER", r"REVISION-PC\SQLEXPRESS")
    database: str = os.getenv("DB_NAME", "EnterpriseProcurementSalesDb")
    driver: str = os.getenv("DB_DRIVER", "ODBC Driver 17 for SQL Server")
    trusted_connection: bool = _env_bool("DB_TRUSTED_CONNECTION", True)
    username: str = os.getenv("DB_USERNAME", "")
    password: str = os.getenv("DB_PASSWORD", "")

    @property
    def connection_string(self) -> str:
        base = (
            f"DRIVER={{{self.driver}}};"
            f"SERVER={self.server};"
            f"DATABASE={self.database};"
        )
        if self.username:
            base += f"UID={self.username};PWD={self.password};"
        elif self.trusted_connection:
            base += "Trusted_Connection=yes;"
        return base


@dataclass(frozen=True)
class LLMConfig:
    enabled: bool = _env_bool("USE_LLM", True)
    provider: str = os.getenv("LLM_PROVIDER", "groq")
    model: str = os.getenv("LLM_MODEL", "llama-3.1-70b-versatile")
    api_key: str = os.getenv("GROQ_API_KEY", "")
    temperature: float = float(os.getenv("LLM_TEMPERATURE", "0.4"))
    max_retries: int = int(os.getenv("LLM_MAX_RETRIES", "2"))


# ---------------------------------------------------------------------------
# HARD domain rules. The generation layer enforces these directly; the LLM
# (when enabled) is only ever used for lexical/creative fields (names,
# descriptions) and every one of its outputs is validated before use.
# ---------------------------------------------------------------------------

DEFAULT_ORDER_STATUSES: List[str] = ["Pending", "Completed", "Cancelled"]
ORDER_CREATION_STATUS: str = "Pending"            # every new PO / SO starts here

VENDOR_INVOICE_STATUSES: List[str] = ["Pending", "Paid"]
VENDOR_INVOICE_CREATION_STATUS: str = "Pending"

USER_ROLE_CODES: List[str] = [
    "Admin", "PO_Creator", "PO_Approver", "Sales_Creator", "Sales_Approver",
]

TARGET_BRANCH_COUNT: int = 10        # BMW Egypt's real dealer/service network (see domain_rules.BMW_EGYPT_BRANCHES)
DATA_YEAR: int = 2026          # all generated dates fall within/prior to this year
DATA_START = datetime(2026, 1, 1, 8, 0, 0)       # datetime objects (generators do date arithmetic on them)
DATA_END = datetime(2026, 11, 6, 17, 0, 0)
# ---------------------------------------------------------------------------
# Row-count targets for tables the agent can't size from FK fan-out alone.
#
# Tables NOT listed here are sized dynamically by their own generator
# instead of this plan (they scale automatically with whatever you set
# above, so you never edit them directly):
#   - purchaseorderitem / salesorderitem / invoiceitem / creditnoteitem:
#       1-5 line items per parent row (generators.py::_item_lines_for)
#   - vendorinvoice: one per "Completed" PurchaseOrder (~55% of purchaseorder)
#   - creditnote: ~8% of salesorder
#   - branchinventory: every branch x a random sample of 10-25 products
#   - userrole: one row per applicationuser
#
# The numbers below are tuned so the FULL pipeline (all 19 tables,
# including the dynamically-sized ones above) lands at ~100,000 total
# rows while keeping exactly the 10 real BMW Egypt branches and exactly the 5 required
# role codes.
# ---------------------------------------------------------------------------
ROW_COUNT_PLAN = {
    "role": len(USER_ROLE_CODES),   # exactly 5 — Admin/PO_Creator/PO_Approver/Sales_Creator/Sales_Approver
    "branch": TARGET_BRANCH_COUNT,  # exactly the 10 real BMW Egypt locations
    "warehouse": TARGET_BRANCH_COUNT,          # one warehouse per branch, min
    "category": 12,
    "product": 800,
    "vendor": 200,
    "customer": 400,
    "applicationuser": 300,         # every user explicitly linked to one of the 10 BMW branches
    "purchaseorder": 6000,
    "salesorder": 9000,
    "salesinvoice": 6000,           # generic-generator table; sized here so it isn't left at the 25-row default
}

DB = DBConfig()
LLM = LLMConfig()
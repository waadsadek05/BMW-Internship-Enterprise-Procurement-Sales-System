"""
domain_rules.py
----------------
Deterministic, zero-hallucination generators AND validators for the
columns the spec calls out by name (Vendor.Contact, Warehouse/Branch
.Location, CreditNote.Reason, InvoiceNumber). These never depend on the
LLM — they are the ground truth that any LLM-suggested value is checked
against before it is allowed anywhere near an INSERT statement.
"""
import re
import random

PLACEHOLDER_PATTERN = re.compile(
    r"^(data|value|item|test|placeholder|string|sample|lorem)[-_ ]?\d*$",
    re.IGNORECASE,
)
DATE_LIKE_PATTERN = re.compile(r"\d{4}-\d{2}-\d{2}")

EGYPT_LOCATIONS = [
    "Downtown Cairo", "Maadi, Cairo", "Nasr City, Cairo", "Heliopolis, Cairo",
    "Zamalek, Cairo", "New Cairo - Fifth Settlement", "6th of October City",
    "Sheikh Zayed City, Giza", "Mohandessin, Giza", "Dokki, Giza",
    "Smouha, Alexandria", "Sidi Gaber, Alexandria", "Miami, Alexandria",
    "Stanley, Alexandria", "Sporting, Alexandria", "Mansoura, Dakahlia",
    "Tanta, Gharbia", "Zagazig, Sharqia", "Ismailia City", "Port Said",
    "Suez City", "Assiut City", "Luxor City", "Aswan City", "Hurghada, Red Sea",
    "Obour City, Qalyubia", "Shubra, Cairo", "Rehab City, New Cairo",
    "Madinaty, Cairo", "El Shorouk City", "Banha, Qalyubia", "Damanhour, Beheira",
    "Faisal, Giza", "Haram, Giza", "Agouza, Giza", "Katameya, New Cairo",
    "Mokattam, Cairo", "Ain Shams, Cairo", "October Gardens, Giza",
    "El Mokattam Industrial Zone, Cairo", "10th of Ramadan City",
]

# BMW Egypt's real dealer/service-center network, per the official
# dealer locator (bmw-egypt.com/en/topics/fascination-bmw/dealer-locator.html),
# verified via web search. There are 10 locations, not 5 -- 3 of them
# (Kattameya, Motor City, Glory Square) are all in the Kattameya area,
# and exactly 1 is in Alexandria.
BMW_EGYPT_BRANCHES = [
    {"name": "BMW Center Kattameya", "location": "Area 10A, Ring Road, Kattameya, Cairo"},
    {"name": "BMW Center Motor City", "location": "Ring Road, Motor City, Kattameya, Cairo"},
    {"name": "BMW Center Glory Square", "location": "Glory Square, Kattameya, Cairo"},
    {"name": "BMW Showroom New Cairo", "location": "Northern 90 Street, New Cairo"},
    {"name": "BMW Center Almaza", "location": "Al-Nasr Road, Almaza, Cairo"},
    {"name": "BMW Showroom Desert Road", "location": "KM 28, Cairo-Alexandria Desert Road, Giza"},
    {"name": "BMW Center Abo Rawash", "location": "Area 31, Industrial Zone, Abo Rawash, Giza"},
    {"name": "BMW Center Alexandria", "location": "Al-Qabari, Al-Basal Port, Alexandria"},
    {"name": "BMW Center Obour", "location": "First Industrial Zone, Obour City, Qalyubia"},
    {"name": "BMW Center North Coast", "location": "Sidi Abdel Rahman, KM 140, North Coast"},
]

CREDIT_NOTE_REASONS = [
    "Returned due to wrong part specification",
    "Defective item received",
    "Shipping damage",
    "Incorrect part number supplied",
    "Customer ordered wrong model compatibility",
    "Part failed quality inspection",
    "Duplicate order - cancelled by customer",
    "Item received with missing components",
    "Warranty replacement return",
    "Excess stock return to vendor",
    "Damaged packaging upon arrival",
    "Part does not match vehicle chassis number",
    "Late delivery - order cancelled by customer",
    "Product recall by manufacturer",
    "Wrong quantity delivered",
    "Part corroded/rusted in storage",
]

AUTOMOTIVE_PRODUCT_CATEGORIES = [
    "Engine Parts", "Brake System", "Suspension & Steering",
    "Electrical & Lighting", "Filters", "Transmission Parts",
    "Cooling System", "Exhaust System", "Body Parts & Panels",
    "Interior Accessories", "Tires & Wheels", "Batteries",
]

# BMW model lines — used to build realistic BMW spare-part product names,
# e.g. "BMW 3 Series Front Brake Pad Set".
BMW_MODELS = [
    "1 Series", "2 Series", "3 Series", "4 Series", "5 Series", "6 Series",
    "7 Series", "8 Series", "X1", "X2", "X3", "X4", "X5", "X6", "X7",
    "Z4", "M2", "M3", "M4", "M5", "i3", "i4", "i7", "iX",
]

# Part-type vocabulary keyed by the EXACT category names in
# AUTOMOTIVE_PRODUCT_CATEGORIES above, so a product's name can stay
# consistent with the category it's actually filed under.
CATEGORY_PART_TYPES = {
    "Engine Parts": [
        "Oil Filter", "Air Filter", "Timing Belt Kit", "Piston Ring Set",
        "Engine Mount", "Spark Plug Set", "Fuel Injector", "Camshaft",
        "Cylinder Head Gasket", "Turbocharger",
    ],
    "Brake System": [
        "Front Brake Pad Set", "Rear Brake Pad Set", "Brake Disc",
        "Brake Caliper", "Brake Master Cylinder", "ABS Sensor",
        "Brake Fluid Reservoir",
    ],
    "Suspension & Steering": [
        "Shock Absorber", "Coil Spring", "Control Arm", "Steering Rack",
        "Tie Rod End", "Stabilizer Link", "Strut Mount",
    ],
    "Electrical & Lighting": [
        "Headlight Assembly", "Tail Light Assembly", "Alternator",
        "Starter Motor", "Battery Cable Set", "Fog Light", "Ignition Coil",
    ],
    "Filters": [
        "Oil Filter", "Cabin Air Filter", "Fuel Filter", "Air Intake Filter",
    ],
    "Transmission Parts": [
        "Clutch Kit", "Transmission Mount", "Gearbox Oil Seal", "Flywheel",
        "Driveshaft",
    ],
    "Cooling System": [
        "Radiator", "Water Pump", "Thermostat", "Cooling Fan",
        "Coolant Reservoir",
    ],
    "Exhaust System": [
        "Exhaust Muffler", "Catalytic Converter", "Exhaust Manifold",
        "Oxygen Sensor",
    ],
    "Body Parts & Panels": [
        "Front Bumper", "Rear Bumper", "Side Mirror", "Door Panel", "Hood",
        "Fender",
    ],
    "Interior Accessories": [
        "Floor Mat Set", "Seat Cover Set", "Steering Wheel Cover",
        "Dashboard Trim Kit",
    ],
    "Tires & Wheels": [
        "Alloy Wheel Rim", "Tire", "Wheel Hub", "Valve Stem",
    ],
    "Batteries": [
        "Car Battery", "Battery Terminal Set", "Battery Cable",
    ],
}
# Flattened fallback pool for when a product's category can't be resolved
# to one of the exact names above (still BMW-relevant, just not filtered
# to one category).
GENERIC_PART_TYPES = sorted({p for parts in CATEGORY_PART_TYPES.values() for p in parts})

VENDOR_EMAIL_DOMAINS = ["autoparts.com", "supply.com.eg", "trading.eg", "spareparts.com"]
EG_MOBILE_PREFIXES = ["010", "011", "012", "015"]

# Realistic-looking (but not identity-derived, by design -- real passwords
# aren't your username) password material: Word + digits + symbol, e.g.
# "Falcon482!". Beats a bare "Serve-1" placeholder without pretending to
# model real password security practices.
PASSWORD_WORDS = [
    "Falcon", "Comet", "Tiger", "River", "Shadow", "Nova", "Atlas", "Orbit",
    "Ember", "Cobalt", "Maple", "Delta", "Raven", "Phoenix", "Horizon",
    "Summit", "Cipher", "Quartz", "Aurora", "Granite",
]
PASSWORD_SYMBOLS = "!@#$%*"


def random_password() -> str:
    word = random.choice(PASSWORD_WORDS)
    digits = random.randint(10, 999)
    symbol = random.choice(PASSWORD_SYMBOLS)
    return f"{word}{digits}{symbol}"


def sku_code(model: str, part: str, seq: int, max_len: int = None, chassis: str = "") -> str:
    """SKU that reads as belonging to its product, e.g. model='3 Series',
    chassis='G20', part='Front Brake Pad Set', seq=7 -> '3S-G20-FBPS-0007'.
    The zero-padded sequence is always kept intact (guaranteeing
    uniqueness) even if max_len forces the descriptive prefix to be trimmed."""
    model_code = re.sub(r"[^A-Za-z0-9]", "", model).upper()[:4] or "GEN"
    words = re.findall(r"[A-Za-z0-9]+", part)
    part_code = "".join(w[0] for w in words).upper() if len(words) > 1 else (words[0][:4].upper() if words else "PRT")
    seq_str = f"{seq:04d}"
    chassis_code = re.sub(r"[^A-Za-z0-9]", "", chassis).upper()
    prefix = "-".join(p for p in (model_code, chassis_code, part_code) if p)
    sku = f"{prefix}-{seq_str}"
    if max_len and len(sku) > max_len:
        avail = max_len - len(seq_str) - 1
        if avail < 1:
            return seq_str[-max_len:]
        sku = f"{prefix.replace('-', '')[:avail]}-{seq_str}"
    return sku


def is_placeholder(value) -> bool:
    if value is None:
        return True
    v = str(value).strip()
    if not v:
        return True
    return bool(PLACEHOLDER_PATTERN.match(v))


def random_egypt_location() -> str:
    base = random.choice(EGYPT_LOCATIONS)
    variant = random.choice(["", "", " - Branch Office", " Commercial District"])
    return f"{base}{variant}".strip()


def area_label(location_base: str) -> str:
    """Short, human place-name for use in a Branch/Warehouse Name field,
    e.g. 'Maadi, Cairo' -> 'Maadi', 'New Cairo - Fifth Settlement' ->
    'New Cairo'. Strips the ", <governorate>" suffix and any
    " - <variant>" suffix random_egypt_location() may have appended."""
    label = location_base.split(",")[0].split(" - ")[0].strip()
    return label or location_base.strip()


def random_egypt_location_pair(suffix_pool=("", "", " - Branch Office", " Commercial District")):
    """Returns (full_location_string, short_area_label) built from the
    SAME base place, so a Name derived from the area always matches the
    Location stored alongside it (e.g. Location='Maadi, Cairo - Branch
    Office', area='Maadi')."""
    base = random.choice(EGYPT_LOCATIONS)
    variant = random.choice(suffix_pool)
    return f"{base}{variant}".strip(), area_label(base)


def random_vendor_contact(seed_name: str = "vendor") -> str:
    """Always a real-looking phone number or email — NEVER a date."""
    if random.random() < 0.5:
        prefix = random.choice(EG_MOBILE_PREFIXES)
        return f"+20 {prefix} {random.randint(1000000, 9999999)}"
    slug = re.sub(r"[^a-z0-9]+", "", seed_name.lower())[:16] or "vendor"
    domain = random.choice(VENDOR_EMAIL_DOMAINS)
    return f"sales@{slug}.{domain.split('.', 1)[-1]}"


def random_credit_note_reason() -> str:
    return random.choice(CREDIT_NOTE_REASONS)


def format_invoice_number(sequence: int, year: int, prefix: str = "INV") -> str:
    return f"{prefix}-{year}-{sequence:05d}"


# --- validators (used by validators.py before every insert) --------------

def validate_contact(value: str) -> bool:
    if is_placeholder(value) or DATE_LIKE_PATTERN.search(str(value)):
        return False
    is_email = "@" in value and "." in value.split("@")[-1]
    is_phone = bool(re.search(r"\d{7,}", value))
    return is_email or is_phone


def validate_location(value: str) -> bool:
    if is_placeholder(value) or DATE_LIKE_PATTERN.search(str(value)):
        return False
    return len(value) >= 3


def validate_reason(value: str) -> bool:
    if is_placeholder(value):
        return False
    return len(str(value).split()) >= 2


def validate_invoice_number(value: str) -> bool:
    return bool(re.match(r"^[A-Z]{2,5}-\d{4}-\d{4,6}$", str(value)))


def random_egypt_mobile() -> str:
    """بإرجاع رقم موبايل مصري عشوائي يبدأ بـ 010 أو 011 أو 012 أو 015"""
    prefix = random.choice(["010", "011", "012", "015"])
    rest = "".join([str(random.randint(0, 9)) for _ in range(8)])
    return prefix + rest


# ---------------------------------------------------------------------------
# BMW model lines with their real chassis/generation codes
# ---------------------------------------------------------------------------
BMW_MODEL_VARIANTS = [
    ("1 Series", "F40"), ("2 Series", "G42"), ("3 Series", "G20"), ("3 Series", "F30"),
    ("4 Series", "G22"), ("5 Series", "G30"), ("5 Series", "G60"), ("7 Series", "G11"),
    ("7 Series", "G70"), ("8 Series", "G15"), ("X1", "F48"), ("X1", "U11"),
    ("X2", "F39"), ("X3", "G01"), ("X3", "F25"), ("X4", "G02"), ("X5", "G05"),
    ("X5", "F15"), ("X6", "G06"), ("X7", "G07"), ("Z4", "G29"), ("M3", "G80"),
    ("M4", "G82"), ("M5", "F90"), ("i4", "G26"), ("iX", "I20"),
]
_PREMIUM_MODELS = {"7 Series", "8 Series", "X5", "X6", "X7", "M3", "M4", "M5", "iX", "i4"}

# (min, max) price in EGP for a base-model part; premium models scale up.
PART_PRICE_RANGES = {
    "Oil Filter": (450, 1500), "Air Filter": (600, 2200), "Cabin Air Filter": (500, 1800),
    "Fuel Filter": (900, 3000), "Air Intake Filter": (1200, 3500),
    "Timing Belt Kit": (6000, 18000), "Piston Ring Set": (8000, 22000),
    "Engine Mount": (2500, 9000), "Spark Plug Set": (2500, 8000),
    "Fuel Injector": (5000, 16000), "Camshaft": (20000, 55000),
    "Cylinder Head Gasket": (3500, 12000), "Turbocharger": (45000, 140000),
    "Front Brake Pad Set": (3500, 9500), "Rear Brake Pad Set": (2800, 8000),
    "Brake Disc": (4500, 14000), "Brake Caliper": (9000, 30000),
    "Brake Master Cylinder": (6000, 18000), "ABS Sensor": (2500, 8500),
    "Brake Fluid Reservoir": (1200, 4000),
    "Shock Absorber": (6000, 22000), "Coil Spring": (3000, 9000),
    "Control Arm": (5000, 17000), "Steering Rack": (30000, 85000),
    "Tie Rod End": (1800, 6000), "Stabilizer Link": (1200, 4000), "Strut Mount": (2200, 7000),
    "Headlight Assembly": (18000, 90000), "Tail Light Assembly": (9000, 40000),
    "Alternator": (15000, 45000), "Starter Motor": (12000, 38000),
    "Battery Cable Set": (1500, 5000), "Fog Light": (3000, 11000), "Ignition Coil": (2500, 7500),
    "Clutch Kit": (14000, 42000), "Transmission Mount": (2500, 8000),
    "Gearbox Oil Seal": (900, 3000), "Flywheel": (15000, 50000), "Driveshaft": (20000, 65000),
    "Radiator": (9000, 32000), "Water Pump": (5000, 16000), "Thermostat": (1800, 6000),
    "Cooling Fan": (6000, 20000), "Coolant Reservoir": (1500, 5500),
    "Exhaust Muffler": (8000, 28000), "Catalytic Converter": (25000, 90000),
    "Exhaust Manifold": (14000, 45000), "Oxygen Sensor": (2500, 8000),
    "Front Bumper": (15000, 55000), "Rear Bumper": (14000, 50000), "Side Mirror": (6000, 25000),
    "Door Panel": (12000, 40000), "Hood": (20000, 65000), "Fender": (8000, 28000),
    "Floor Mat Set": (2500, 9000), "Seat Cover Set": (6000, 22000),
    "Steering Wheel Cover": (800, 3500), "Dashboard Trim Kit": (3500, 14000),
    "Alloy Wheel Rim": (12000, 45000), "Tire": (6000, 22000),
    "Wheel Hub": (4500, 14000), "Valve Stem": (150, 600),
    "Car Battery": (6000, 15000), "Battery Terminal Set": (500, 1800), "Battery Cable": (900, 3200),
}


def random_part_price(part: str, model: str = "") -> float:
    """Realistic EGP retail price for a BMW part; premium/M/X7 models cost more."""
    lo, hi = PART_PRICE_RANGES.get(part, (1000, 12000))
    price = random.uniform(lo, hi)
    if model in _PREMIUM_MODELS:
        price *= random.uniform(1.25, 1.6)
    return round(price, 2)


# ---------------------------------------------------------------------------
# Egyptian people names (romanized) -- Name, Email and UserName for a user
# are ALL derived from the same (first, last) pair.
# ---------------------------------------------------------------------------
EG_FIRST_NAMES = [
    "Ahmed", "Mohamed", "Mahmoud", "Mostafa", "Omar", "Youssef", "Karim", "Tarek",
    "Hossam", "Khaled", "Amr", "Ali", "Hassan", "Ibrahim", "Sherif", "Walid",
    "Hany", "Wael", "Ayman", "Sameh", "Ramy", "Adel", "Nader", "Fady",
    "Mona", "Sara", "Nour", "Heba", "Dina", "Rania", "Mariam", "Nada",
    "Yasmin", "Salma", "Aya", "Farah", "Hana", "Reem", "Noha", "Eman",
]
EG_LAST_NAMES = [
    "Hassan", "Ibrahim", "Mahmoud", "Salem", "Fouad", "Nabil", "Abdelrahman", "Saad",
    "Mansour", "Farouk", "Gamal", "Zaki", "Shehata", "Soliman", "Hegazy", "Ragab",
    "Elsayed", "Abdelaziz", "Metwally", "Khalil", "Mostafa", "Tawfik", "Amin", "Fahmy",
    "Lotfy", "Said", "Badawy", "Ezzat", "Morsy", "Sabry", "Kamel", "Younis",
]
EG_COMPANY_SUFFIXES = ["Auto Parts", "Motors", "Trading", "Spare Parts Co.", "Car Services", "Automotive"]


def random_person():
    return random.choice(EG_FIRST_NAMES), random.choice(EG_LAST_NAMES)

USER_EMAIL_DOMAIN = "bmw-egypt-dealers.example.com"   # .example.com = reserved, never a real mailbox
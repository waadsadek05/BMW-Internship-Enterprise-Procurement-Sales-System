"""Runs the REAL pipeline against an in-memory fake of the 19-table schema (no SQL Server needed)."""
import sys, types, collections, logging
sys.modules['pyodbc'] = types.SimpleNamespace(Connection=object, Cursor=object, Error=Exception, ProgrammingError=Exception, connect=None)
import main
from schema_introspector import TableInfo, ColumnInfo, ForeignKey

def T(name, cols, pk, fks=()):
    t = TableInfo(name=name); 
    for c in cols:
        n, ty, *rest = c; ident = 'identity' in rest
        t.columns[n] = ColumnInfo(n, ty, 450 if ty == 'nvarchar' else None, False, ident)
    t.primary_key = [pk]; t.foreign_keys = [ForeignKey(name, c, r, 'Id') for c, r in fks]; return t
I = lambda n='Id': (n, 'int', 'identity')
S = lambda n: (n, 'nvarchar'); N = lambda n: (n, 'int'); D = lambda n: (n, 'datetime2'); M = lambda n: (n, 'decimal')
schema = {t.name: t for t in [
 T('Roles', [S('Id'), S('Name')], 'Id'),
 T('Branches', [I(), S('Name'), S('Location')], 'Id'),
 T('Warehouses', [I(), S('Name'), S('Location'), N('BranchId')], 'Id', [('BranchId','Branches')]),
 T('Categories', [I(), S('Name')], 'Id'),
 T('Products', [I(), S('Name'), S('SKU'), M('Price'), N('CategoryId')], 'Id', [('CategoryId','Categories')]),
 T('Vendors', [I(), S('Name'), S('Contact')], 'Id'),
 T('Customers', [I(), S('Name'), S('Email'), S('Phone')], 'Id'),
 T('AppApplicationUser', [S('Id'), S('FullName'), S('Email'), S('UserName'), S('PasswordHash'), N('BranchId')], 'Id', [('BranchId','Branches')]),
 T('UserRoles', [I(), S('UserId'), S('RoleId')], 'Id', [('UserId','AppApplicationUser'),('RoleId','Roles')]),
 T('PurchaseOrders', [I(), N('VendorId'), N('BranchId'), S('CreatedById'), S('ApprovedById'), D('OrderDate'), S('Status'), M('TotalAmount')], 'Id',
   [('VendorId','Vendors'),('BranchId','Branches'),('CreatedById','AppApplicationUser'),('ApprovedById','AppApplicationUser')]),
 T('PurchaseOrderItems', [I(), N('PurchaseOrderId'), N('ProductId'), N('Quantity'), M('UnitPrice')], 'Id', [('PurchaseOrderId','PurchaseOrders'),('ProductId','Products')]),
 T('VendorInvoices', [I(), N('PurchaseOrderId'), S('InvoiceNumber'), D('InvoiceDate'), S('Status'), M('TotalAmount')], 'Id', [('PurchaseOrderId','PurchaseOrders')]),
 T('SalesOrders', [I(), N('CustomerId'), N('BranchId'), S('CreatedById'), D('OrderDate'), S('Status'), M('TotalAmount')], 'Id',
   [('CustomerId','Customers'),('BranchId','Branches'),('CreatedById','AppApplicationUser')]),
 T('SalesOrderItems', [I(), N('SalesOrderId'), N('ProductId'), N('Quantity'), M('UnitPrice')], 'Id', [('SalesOrderId','SalesOrders'),('ProductId','Products')]),
 T('SalesInvoices', [I(), N('SalesOrderId'), S('InvoiceNumber'), D('InvoiceDate'), M('TotalAmount')], 'Id', [('SalesOrderId','SalesOrders')]),
 T('InvoiceItems', [I(), N('SalesInvoiceId'), N('ProductId'), N('Quantity'), M('UnitPrice')], 'Id', [('SalesInvoiceId','SalesInvoices'),('ProductId','Products')]),
 T('CreditNotes', [I(), N('SalesInvoiceId'), S('Reason'), D('ReturnDate'), M('RefundAmount')], 'Id', [('SalesInvoiceId','SalesInvoices')]),
 T('CreditNoteItems', [I(), N('CreditNoteId'), N('ProductId'), N('Quantity'), M('UnitPrice')], 'Id', [('CreditNoteId','CreditNotes'),('ProductId','Products')]),
 T('BranchInventories', [I(), N('WarehouseId'), N('ProductId'), N('StockQuantity')], 'Id', [('WarehouseId','Warehouses'),('ProductId','Products')]),
]}
assert len(schema) == 19
DB = collections.defaultdict(list)
class FakeIntro:
    def __init__(s, c): pass
    def build_schema(s): return schema
main.SchemaIntrospector = FakeIntro
class FakeConn:
    def commit(s): pass
    def rollback(s): pass
    def close(s): pass
main.pyodbc.connect = lambda *a, **k: FakeConn()
def fake_insert(conn, ti, rows):
    ids = []
    for r in rows:
        r = dict(r)
        if ti.identity_columns:
            r[ti.identity_columns[0]] = len(DB[ti.name]) + 1; ids.append(r[ti.identity_columns[0]])
        DB[ti.name].append(r)
    return ids
main.insert_rows = fake_insert
def fake_totals(conn, gen):
    for ti, col, pk, pairs in gen.header_total_updates():
        idx = {r[pk]: r for r in DB[ti.name]}
        for total, k in pairs: idx[k][col] = total
main.apply_header_totals = fake_totals
logging.disable(logging.CRITICAL)
main.run()
logging.disable(logging.NOTSET)

# ----------------------------- checks -----------------------------
ok = True
def check(name, cond):
    global ok; ok &= bool(cond); print(('PASS ' if cond else 'FAIL ') + name)
total = sum(len(v) for v in DB.values()); print('TOTAL ROWS', total)
for t, v in DB.items(): print(f'  {t:22}{len(v):>7}')
check('10 branches', len(DB['Branches']) == 10)
check('roles exactly 5 in order', [r['Name'] for r in DB['Roles']] == main.dr.__dict__.get('X', ["Admin","PO_Creator","PO_Approver","Sales_Creator","Sales_Approver"]))
check('role ids 1..5', [r['Id'] for r in DB['Roles']] == ['1','2','3','4','5'])
check('user ids 1..N', [u['Id'] for u in DB['AppApplicationUser']] == [str(i) for i in range(1, len(DB['AppApplicationUser'])+1)])
u = DB['AppApplicationUser']
check('email/username match full name', all(x['Email'].split('@')[0] == x['UserName'] and x['UserName'].replace('.', ' ').split(' ')[0] == x['FullName'].split(' ')[0].lower() for x in u))
check('every user has branch', all(x['BranchId'] in range(1, 11) for x in u))
ur = DB['UserRoles']; role_of = {x['UserId']: x['RoleId'] for x in ur}
check('1 role per user', len(ur) == len(u) and len(role_of) == len(u))
check('all 5 roles used', set(role_of.values()) == {'1','2','3','4','5'})
check('vendor contact valid', all(main.dr.validate_contact(v['Contact']) for v in DB['Vendors']))
check('locations valid', all(main.dr.validate_location(b['Location']) for b in DB['Branches'] + DB['Warehouses']))
check('credit reasons valid', all(main.dr.validate_reason(c['Reason']) for c in DB['CreditNotes']))
check('invoice numbers valid', all(main.dr.validate_invoice_number(i['InvoiceNumber']) for i in DB['SalesInvoices'] + DB['VendorInvoices']))
check('PO/SO statuses', {p['Status'] for p in DB['PurchaseOrders'] + DB['SalesOrders']} <= {'Pending','Completed','Cancelled'})
check('VI statuses', {p['Status'] for p in DB['VendorInvoices']} <= {'Pending','Paid'})
for t, c in (('PurchaseOrders','OrderDate'),('SalesOrders','OrderDate'),('SalesInvoices','InvoiceDate'),('VendorInvoices','InvoiceDate'),('CreditNotes','ReturnDate')):
    d = [r[c] for r in DB[t]]; check(f'{t}.{c} non-decreasing & in 2026', all(a <= b for a, b in zip(d, d[1:])) and all(x.year == 2026 for x in d))
# FK integrity
for t, ti in schema.items():
    for fk in ti.foreign_keys:
        ids = {r['Id'] for r in DB[fk.ref_table]}
        check(f'FK {t}.{fk.fk_column}->{fk.ref_table}', all(r[fk.fk_column] in ids for r in DB[t] if r[fk.fk_column] is not None))
# line consistency
def lines(tbl, key): 
    d = collections.defaultdict(dict)
    for r in DB[tbl]: d[r[key]][r['ProductId']] = (r['Quantity'], r['UnitPrice'])
    return d
poi, soi = lines('PurchaseOrderItems','PurchaseOrderId'), lines('SalesOrderItems','SalesOrderId')
ii, cni = lines('InvoiceItems','SalesInvoiceId'), lines('CreditNoteItems','CreditNoteId')
si = {r['Id']: r for r in DB['SalesInvoices']}
check('invoice items == SO items', all(ii[i] == soi[si[i]['SalesOrderId']] for i in si))
cn = {r['Id']: r for r in DB['CreditNotes']}
check('credit note items subset of invoice items, qty<=sold, same price', all(p in ii[cn[c]['SalesInvoiceId']] and q <= ii[cn[c]['SalesInvoiceId']][p][0] and pr == ii[cn[c]['SalesInvoiceId']][p][1] for c, d in cni.items() for p, (q, pr) in d.items()))
check('credit refund == sum of lines', all(abs(cn[c]['RefundAmount'] - sum(q*p for q, p in d.values())) < 0.02 for c, d in cni.items()))
check('SI total == SO lines', all(abs(si[i]['TotalAmount'] - sum(q*p for q, p in soi[si[i]['SalesOrderId']].values())) < 0.02 for i in si))
vi = DB['VendorInvoices']; po = {r['Id']: r for r in DB['PurchaseOrders']}
check('VI only for Completed PO', all(po[v['PurchaseOrderId']]['Status'] == 'Completed' for v in vi))
check('VI total == PO lines', all(abs(v['TotalAmount'] - sum(q*p for q, p in poi[v['PurchaseOrderId']].values())) < 0.02 for v in vi))
check('PO header total == lines', all(abs(po[k]['TotalAmount'] - sum(q*p for q, p in d.values())) < 0.02 for k, d in poi.items()))
so = {r['Id']: r for r in DB['SalesOrders']}
check('SO header total == lines', all(abs(so[k]['TotalAmount'] - sum(q*p for q, p in d.values())) < 0.02 for k, d in soi.items()))
prod = {r['Id']: r['Price'] for r in DB['Products']}
check('SO unit price == product price', all(abs(p - prod[pid]) < 0.01 for d in soi.values() for pid, (q, p) in d.items()))
check('PO cost < selling price', all(p < prod[pid] for d in poi.values() for pid, (q, p) in d.items()))
# inventory == received via Completed POs, per branch+product
exp = collections.Counter()
for k, d in poi.items():
    if po[k]['Status'] == 'Completed':
        for pid, (q, p) in d.items(): exp[(po[k]['BranchId'], pid)] += q
wh = {w['Id']: w['BranchId'] for w in DB['Warehouses']}
got = collections.Counter()
for r in DB['BranchInventories']: got[(wh[r['WarehouseId']], r['ProductId'])] += r['StockQuantity']
check('inventory == qty received by completed POs (branch x product)', exp == got)
cr = {x['Id']: x for x in DB['AppApplicationUser']}
check('PO creator is PO_Creator at PO branch', all(role_of[p['CreatedById']] == '2' and cr[p['CreatedById']]['BranchId'] == p['BranchId'] for p in po.values()))
check('PO approver is PO_Approver', all(p['ApprovedById'] is None or role_of[p['ApprovedById']] == '3' for p in po.values()))
check('SO creator is Sales_Creator at SO branch', all(role_of[s['CreatedById']] == '4' and cr[s['CreatedById']]['BranchId'] == s['BranchId'] for s in so.values()))
print('SAMPLE USERS', [(x['Id'], x['FullName'], x['Email'], x['UserName'], role_of[x['Id']], x['BranchId']) for x in u[:3]])
print('SAMPLE PRODUCTS', [(p['Name'], p['SKU'], p['Price']) for p in DB['Products'][:3]])
print('ALL OK' if ok else 'SOME FAILED')

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using EnterpriseSystem.Models;

namespace EnterpriseSystem.Controllers
{
    public class CreditNotesController : Controller
    {
        private readonly EnterpriseProcurementSalesDbContext _context;

        public CreditNotesController(EnterpriseProcurementSalesDbContext context)
        {
            _context = context;
        }

        // GET: CreditNotes
        public async Task<IActionResult> Index(string searchTerm, int page = 1)
        {
            int pageSize = 50;

            // الاستعلام مع عمل Include لـ SalesInvoice لجلب بياناته بأسلوب صحيح
            var query = _context.CreditNotes
                .Include(c => c.SalesInvoice)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                string term = searchTerm.ToLower().Trim();

                // 1. جلب البيانات من قاعدة البيانات أولاً بالترتيب التنازلي
                var allData = await query.OrderByDescending(c => c.Id).ToListAsync();

                // 2. الفلترة في الذاكرة لتغطي جميع الأعمدة، التواريخ، ومبلغ الاسترداد وعلاقة SalesInvoice بدقة
                var filteredData = allData.Where(c => 
                    c.Id.ToString() == term || 
                    c.Id.ToString().Contains(term) || 
                    (c.Reason != null && c.Reason.ToLower().Contains(term)) ||
                    c.RefundAmount.ToString().Contains(term) ||
                    c.ReturnDate.ToString("dd/MM/yyyy").Contains(term) ||
                    c.ReturnDate.ToString("yyyy-MM-dd").Contains(term) ||
                    c.ReturnDate.ToString().Contains(term) ||
                    (c.SalesInvoice != null && c.SalesInvoice.Id.ToString().Contains(term)) ||
                    (c.SalesInvoice != null && c.SalesInvoice.InvoiceNumber != null && c.SalesInvoice.InvoiceNumber.ToLower().Contains(term))
                ).ToList();

                int totalItems = filteredData.Count;
                int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
                totalPages = totalPages > 0 ? totalPages : 1;
                page = Math.Max(1, Math.Min(page, totalPages));

                var creditNotes = filteredData
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = totalPages;
                ViewBag.SearchTerm = searchTerm;

                return View(creditNotes);
            }
            else
            {
                // الحالة العادية (بدون بحث)
                int totalItems = await query.CountAsync();
                int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
                totalPages = totalPages > 0 ? totalPages : 1;
                page = Math.Max(1, Math.Min(page, totalPages));

                var creditNotes = await query
                    .OrderByDescending(c => c.Id)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = totalPages;
                ViewBag.SearchTerm = searchTerm;

                return View(creditNotes);
            }
        }

        /// GET: CreditNotes/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var creditNote = await _context.CreditNotes
                .Include(c => c.SalesInvoice)
                .Include(c => c.CreditNoteItems)
                    .ThenInclude(ci => ci.Product) // لجلب اسم المنتج
                .FirstOrDefaultAsync(m => m.Id == id);

            if (creditNote == null)
            {
                return NotFound();
            }

            return View(creditNote);
        }

        [HttpGet]
        public async Task<IActionResult> GetInvoiceItems(int invoiceId)
        {
            // 1. جلب عناصر الفاتورة الأساسية
            var invoiceItems = await _context.InvoiceItems
                .Where(ii => ii.SalesInvoiceId == invoiceId)
                .Include(ii => ii.Product)
                .Select(ii => new {
                    productId = ii.ProductId,
                    productName = ii.Product != null ? (ii.Product.Name ?? ii.Product.Sku) : "Product #" + ii.ProductId,
                    quantity = ii.Quantity,
                    unitPrice = ii.UnitPrice
                })
                .ToListAsync();

            if (invoiceItems == null || !invoiceItems.Any())
            {
                var invoiceWithOrder = await _context.SalesInvoices
                    .Include(s => s.SalesOrder)
                        .ThenInclude(so => so.SalesOrderItems)
                            .ThenInclude(soi => soi.Product)
                    .FirstOrDefaultAsync(s => s.Id == invoiceId);

                if (invoiceWithOrder?.SalesOrder?.SalesOrderItems != null)
                {
                    invoiceItems = invoiceWithOrder.SalesOrder.SalesOrderItems
                        .Select(ii => new {
                            productId = ii.ProductId,
                            productName = ii.Product != null ? (ii.Product.Name ?? ii.Product.Sku) : "Product #" + ii.ProductId,
                            quantity = ii.Quantity,
                            unitPrice = ii.UnitPrice
                        })
                        .ToList();
                }
            }

            var uniqueItems = invoiceItems
                .GroupBy(x => x.productId)
                .Select(g => new {
                    productId = g.Key,
                    productName = g.First().productName,
                    quantity = g.Sum(x => x.quantity),
                    unitPrice = g.First().unitPrice
                })
                .ToList();

            // 2. حساب الكميات التي تم إرجاعها مسبقاً لهذه الفاتورة عبر الـ Credit Notes السابقة
            var resultList = new List<object>();

            foreach (var item in uniqueItems)
            {
                var previouslyReturnedQty = await _context.CreditNoteItems
                    .Where(cni => cni.CreditNote.SalesInvoiceId == invoiceId && cni.ProductId == item.productId)
                    .SumAsync(cni => (int?)cni.Quantity) ?? 0;

                // الكمية المتاحة الحقيقية للإرجاع
                int availableQty = item.quantity - previouslyReturnedQty;

                // إذا كانت الكمية المتاحة أكبر من صفر، نضيفها للقائمة لكي تظهر للمستخدم
                if (availableQty > 0)
                {
                    resultList.Add(new {
                        productId = item.productId,
                        productName = item.productName,
                        quantity = availableQty, // ستظهر الكمية المتبقية فقط (مثلاً لو أُرجعت كلها ستصبح 0 ولن تظهر)
                        unitPrice = item.unitPrice
                    });
                }
            }

            return Json(resultList);
        }

        // GET: CreditNotes/Create
        public IActionResult Create()
        {
            ViewData["SalesInvoiceId"] = new SelectList(_context.SalesInvoices, "Id", "InvoiceNumber");
            return View();
        }

        // POST: CreditNotes/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        // POST: CreditNotes/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreditNote creditNote)
        {
            var invoice = await _context.SalesInvoices
                .Include(i => i.InvoiceItems)
                .FirstOrDefaultAsync(i => i.Id == creditNote.SalesInvoiceId);

            if (invoice == null)
                return Json(new { success = false, message = "Selected invoice not found." });

            // قراءة العناصر المرسلة يدوياً من الـ Request.Form لتجنب أي مشاكل في الـ Validation
            var validItemsToReturn = new List<CreditNoteItem>();
            var activeSelections = new List<(int ProductId, int Quantity, decimal UnitPrice)>();
            int index = 0;

            while (true)
            {
                var prodIdVal = Request.Form[$"submittedItems[{index}].ProductId"];
                var qtyVal = Request.Form[$"submittedItems[{index}].Quantity"];
                var unitPriceVal = Request.Form[$"submittedItems[{index}].UnitPrice"];

                if (string.IsNullOrEmpty(prodIdVal))
                    break;

                if (int.TryParse(prodIdVal, out int prodId) && int.TryParse(qtyVal, out int qty) && qty > 0)
                {
                    decimal.TryParse(unitPriceVal, out decimal unitPrice);
                    activeSelections.Add((prodId, qty, unitPrice));
                }
                index++;
            }

            if (!activeSelections.Any())
                return Json(new { success = false, message = "Please select at least one item quantity to return." });

            decimal calculatedRefundAmount = 0;

            foreach (var selection in activeSelections)
            {
                var invoiceItem = invoice.InvoiceItems.FirstOrDefault(i => i.ProductId == selection.ProductId);
                if (invoiceItem == null)
                    return Json(new { success = false, message = "Product was not part of this invoice." });

                var previouslyReturnedQty = await _context.CreditNoteItems
                    .Where(cni => cni.CreditNote.SalesInvoiceId == invoice.Id && cni.ProductId == selection.ProductId)
                    .SumAsync(cni => (int?)cni.Quantity) ?? 0;

                int maxAllowedReturn = invoiceItem.Quantity - previouslyReturnedQty;
                if (selection.Quantity > maxAllowedReturn)
                    return Json(new { success = false, message = $"Return quantity exceeds allowed limit ({maxAllowedReturn})." });

                decimal itemTotal = selection.Quantity * invoiceItem.UnitPrice;
                calculatedRefundAmount += itemTotal;

                validItemsToReturn.Add(new CreditNoteItem
                {
                    ProductId = selection.ProductId,
                    Quantity = selection.Quantity,
                    UnitPrice = invoiceItem.UnitPrice
                });
            }

            creditNote.RefundAmount = calculatedRefundAmount;
            creditNote.ReturnDate = DateTime.Now;

            // مسح الـ ModelState لتجاوز أي قيود تحقق تلقائية غير مطلوبة
            ModelState.Clear();

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                creditNote.CreditNoteItems = validItemsToReturn;
                
                _context.CreditNotes.Add(creditNote);
                await _context.SaveChangesAsync();

                // تحديث المخزون
                foreach (var returnedItem in validItemsToReturn)
                {
                    var inventory = await _context.BranchInventories
                        .FirstOrDefaultAsync(i => i.ProductId == returnedItem.ProductId);

                    if (inventory != null)
                    {
                        inventory.StockQuantity += returnedItem.Quantity;
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Json(new { success = true }); 
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, message = "Database Error: " + ex.Message });
            }
        }

        // GET: CreditNotes/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var creditNote = await _context.CreditNotes.FindAsync(id);
            if (creditNote == null)
            {
                return NotFound();
            }
            ViewData["SalesInvoiceId"] = new SelectList(_context.SalesInvoices, "Id", "InvoiceNumber", creditNote.SalesInvoiceId);
            return View(creditNote);
        }

        // POST: CreditNotes/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,SalesInvoiceId,ReturnDate,Reason,RefundAmount")] CreditNote creditNote)
        {
            if (id != creditNote.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(creditNote);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!CreditNoteExists(creditNote.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            ViewData["SalesInvoiceId"] = new SelectList(_context.SalesInvoices, "Id", "InvoiceNumber", creditNote.SalesInvoiceId);
            return View(creditNote);
        }

        // GET: CreditNotes/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var creditNote = await _context.CreditNotes
                .Include(c => c.SalesInvoice)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (creditNote == null)
            {
                return NotFound();
            }

            return View(creditNote);
        }

        // POST: CreditNotes/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var creditNote = await _context.CreditNotes.FindAsync(id);
            if (creditNote != null)
            {
                _context.CreditNotes.Remove(creditNote);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool CreditNoteExists(int id)
        {
            return _context.CreditNotes.Any(e => e.Id == id);
        }
    }
}

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
    public class CreditNoteItemsController : Controller
    {
        private readonly EnterpriseProcurementSalesDbContext _context;

        public CreditNoteItemsController(EnterpriseProcurementSalesDbContext context)
        {
            _context = context;
        }

        // GET: CreditNoteItems
        public async Task<IActionResult> Index()
        {
            var enterpriseProcurementSalesDbContext = _context.CreditNoteItems.Include(c => c.CreditNote).Include(c => c.Product);
            return View(await enterpriseProcurementSalesDbContext.ToListAsync());
        }

        // GET: CreditNoteItems/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var creditNoteItem = await _context.CreditNoteItems
                .Include(c => c.CreditNote)
                .Include(c => c.Product)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (creditNoteItem == null)
            {
                return NotFound();
            }

            return View(creditNoteItem);
        }

        // GET: CreditNoteItems/Create
        public IActionResult Create()
        {
            ViewData["CreditNoteId"] = new SelectList(_context.CreditNotes, "Id", "Id");
            ViewData["ProductId"] = new SelectList(_context.Products, "Id", "Id");
            return View();
        }

        // POST: CreditNoteItems/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,SalesInvoiceId,Reason,RefundAmount")] CreditNote creditNote)
        {
            // 1. جلب الفاتورة المرتبطة للتأكد من إجمالي المبلغ
            var invoice = await _context.SalesInvoices
                .Include(i => i.SalesOrder)
                    .ThenInclude(o => o.SalesOrderItems)
                .FirstOrDefaultAsync(i => i.Id == creditNote.SalesInvoiceId);

            if (invoice == null)
            {
                ModelState.AddModelError("SalesInvoiceId", "Selected invoice does not exist.");
            }
            else
            {
                // 2. التحقق من أن مبلغ الاسترداد لا يتجاوز إجمالي الفاتورة
                if (creditNote.RefundAmount > invoice.TotalAmount)
                {
                    ModelState.AddModelError("RefundAmount", $"Refund amount cannot exceed the total invoice amount ({invoice.TotalAmount:C}).");
                }
                
                // 3. (اختياري) يمكنك تعيين المبلغ تلقائياً لو أردت أن يكون مطابقاً لإجمالي الفاتورة مباشرة
                // creditNote.RefundAmount = invoice.TotalAmount;
            }

            ModelState.Remove("SalesInvoice");

            if (ModelState.IsValid)
            {
                _context.Add(creditNote);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            ViewData["SalesInvoiceId"] = new SelectList(_context.SalesInvoices, "Id", "InvoiceNumber", creditNote.SalesInvoiceId);
            return View(creditNote);
        }

        // GET: CreditNoteItems/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var creditNoteItem = await _context.CreditNoteItems.FindAsync(id);
            if (creditNoteItem == null)
            {
                return NotFound();
            }
            ViewData["CreditNoteId"] = new SelectList(_context.CreditNotes, "Id", "Id", creditNoteItem.CreditNoteId);
            ViewData["ProductId"] = new SelectList(_context.Products, "Id", "Id", creditNoteItem.ProductId);
            return View(creditNoteItem);
        }

        // POST: CreditNoteItems/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,CreditNoteId,ProductId,Quantity,UnitPrice")] CreditNoteItem creditNoteItem)
        {
            if (id != creditNoteItem.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(creditNoteItem);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!CreditNoteItemExists(creditNoteItem.Id))
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
            ViewData["CreditNoteId"] = new SelectList(_context.CreditNotes, "Id", "Id", creditNoteItem.CreditNoteId);
            ViewData["ProductId"] = new SelectList(_context.Products, "Id", "Id", creditNoteItem.ProductId);
            return View(creditNoteItem);
        }

        // GET: CreditNoteItems/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var creditNoteItem = await _context.CreditNoteItems
                .Include(c => c.CreditNote)
                .Include(c => c.Product)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (creditNoteItem == null)
            {
                return NotFound();
            }

            return View(creditNoteItem);
        }

        // POST: CreditNoteItems/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var creditNoteItem = await _context.CreditNoteItems.FindAsync(id);
            if (creditNoteItem != null)
            {
                _context.CreditNoteItems.Remove(creditNoteItem);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool CreditNoteItemExists(int id)
        {
            return _context.CreditNoteItems.Any(e => e.Id == id);
        }
    }
}

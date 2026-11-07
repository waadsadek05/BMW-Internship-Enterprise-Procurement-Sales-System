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
    public class VendorInvoicesController : Controller
    {
        private readonly EnterpriseProcurementSalesDbContext _context;

        public VendorInvoicesController(EnterpriseProcurementSalesDbContext context)
        {
            _context = context;
        }

        // GET: VendorInvoices
        public async Task<IActionResult> Index(string searchTerm, int page = 1)
        {
            int pageSize = 50;

            // الاستعلام مع عمل Include لـ PurchaseOrder لجلب بياناته بأسلوب صحيح
            var query = _context.VendorInvoices
                .Include(v => v.PurchaseOrder)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                string term = searchTerm.ToLower().Trim();

                // 1. جلب البيانات من قاعدة البيانات أولاً
                var allData = await query.OrderByDescending(v => v.Id).ToListAsync();

                // 2. الفلترة في الذاكرة لتغطي جميع الأعمدة الظاهرة في الـ UI والتواريخ بدقة
                var filteredData = allData.Where(v => 
                    v.Id.ToString() == term || 
                    v.Id.ToString().Contains(term) || 
                    // InvoiceNumber
                    (v.InvoiceNumber != null && v.InvoiceNumber.ToLower().Contains(term)) ||
                    // Status
                    (v.Status != null && v.Status.ToLower().Contains(term)) ||
                    // TotalAmount
                    v.TotalAmount.ToString().Contains(term) ||
                    // PurchaseOrder (البحث برقم الـ PO المرتبط)
                    (v.PurchaseOrder != null && v.PurchaseOrder.Id.ToString().Contains(term)) ||
                    // InvoiceDate (دعم البحث بصيغ التاريخ المختلفة)
                    v.InvoiceDate.ToString("dd/MM/yyyy").Contains(term) ||
                    v.InvoiceDate.ToString("yyyy-MM-dd").Contains(term) ||
                    v.InvoiceDate.ToString().Contains(term)
                ).ToList();

                int totalItems = filteredData.Count;
                int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
                totalPages = totalPages > 0 ? totalPages : 1;
                page = Math.Max(1, Math.Min(page, totalPages));

                var invoices = filteredData
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = totalPages;
                ViewBag.SearchTerm = searchTerm;

                return View(invoices);
            }
            else
            {
                // الحالة العادية (بدون بحث)
                int totalItems = await query.CountAsync();
                int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
                totalPages = totalPages > 0 ? totalPages : 1;
                page = Math.Max(1, Math.Min(page, totalPages));

                var invoices = await query
                    .OrderByDescending(v => v.Id)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = totalPages;
                ViewBag.SearchTerm = searchTerm;

                return View(invoices);
            }
        }

        // GET: VendorInvoices/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var vendorInvoice = await _context.VendorInvoices
                .Include(v => v.PurchaseOrder)
                    .ThenInclude(po => po.Vendor)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (vendorInvoice == null)
            {
                return NotFound();
            }

            return View(vendorInvoice);
        }

        // GET: VendorInvoices/Create
        public IActionResult Create()
        {
            ViewData["PurchaseOrderId"] = new SelectList(_context.PurchaseOrders, "Id", "Id");
            return View();
        }

        // POST: VendorInvoices/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("PurchaseOrderId,InvoiceDate,TotalAmount")] VendorInvoice vendorInvoice)
        {
            if (vendorInvoice.InvoiceDate > DateTime.Now)
            {
                ModelState.AddModelError("InvoiceDate", "لا يمكن اختيار تاريخ أو وقت في المستقبل!");
            }

            // 1. توليد رقم فاتورة تلقائي
            var lastInvoice = await _context.VendorInvoices
                .OrderByDescending(i => i.Id)
                .FirstOrDefaultAsync();

            int nextId = lastInvoice != null ? lastInvoice.Id + 1 : 1;
            vendorInvoice.InvoiceNumber = $"INV-{nextId:D3}";
            
            // 2. تعيين الحالة افتراضياً
            vendorInvoice.Status = "Pending";

            ModelState.Remove("InvoiceNumber");
            ModelState.Remove("Status");
            ModelState.Remove("PurchaseOrder");

            if (ModelState.IsValid)
            {
                _context.Add(vendorInvoice);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            ViewData["PurchaseOrderId"] = new SelectList(_context.PurchaseOrders, "Id", "Id", vendorInvoice.PurchaseOrderId);
            return View(vendorInvoice);
        }

        // GET: VendorInvoices/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var vendorInvoice = await _context.VendorInvoices.FindAsync(id);
            if (vendorInvoice == null)
            {
                return NotFound();
            }
            ViewData["PurchaseOrderId"] = new SelectList(_context.PurchaseOrders, "Id", "Id", vendorInvoice.PurchaseOrderId);
            return View(vendorInvoice);
        }

        // POST: VendorInvoices/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,PurchaseOrderId,InvoiceNumber,InvoiceDate,TotalAmount,Status")] VendorInvoice vendorInvoice)
        {
            if (id != vendorInvoice.Id)
            {
                return NotFound();
            }

            // 1. جلب الفاتورة القديمة مع أمر الشراء والمنتجات المرتبطة به من قاعدة البيانات
            var existingInvoice = await _context.VendorInvoices
                .Include(v => v.PurchaseOrder)
                    .ThenInclude(po => po.PurchaseOrderItems)
                .FirstOrDefaultAsync(v => v.Id == id);

            if (existingInvoice == null)
            {
                return NotFound();
            }

            // حفظ الحالة القديمة
            string oldStatus = existingInvoice.Status ?? string.Empty;

            // تحديث البيانات الجديدة
            existingInvoice.InvoiceDate = vendorInvoice.InvoiceDate;
            existingInvoice.TotalAmount = vendorInvoice.TotalAmount;
            existingInvoice.Status = vendorInvoice.Status;

            if (ModelState.IsValid)
            {
                try
                {
                    // 2. التحقق: لو الحالة أصبحت مدفوعة ولم تكن كذلك من قبل
                    bool isNowPaid = string.Equals(existingInvoice.Status, "Paid", StringComparison.OrdinalIgnoreCase) || 
                                    string.Equals(existingInvoice.Status, "paid", StringComparison.OrdinalIgnoreCase);

                    bool wasPaidBefore = string.Equals(oldStatus, "Paid", StringComparison.OrdinalIgnoreCase) || 
                                        string.Equals(oldStatus, "paid", StringComparison.OrdinalIgnoreCase);

                    if (isNowPaid && !wasPaidBefore)
                    {
                        var purchaseOrder = existingInvoice.PurchaseOrder;

                        if (purchaseOrder != null)
                        {
                            // جلب المستودع الخاص بالفرع المرتبط بأمر الشراء فقط حصرياً
                            var warehouse = await _context.Warehouses
                                .FirstOrDefaultAsync(w => w.BranchId == purchaseOrder.BranchId);

                            // التحقق الصارم: إذا لم يكن للفرع مستودع مسجل، نوقف العملية ونعرض خطأ بالإنجليزية
                            if (warehouse == null)
                            {
                                ModelState.AddModelError(string.Empty, $"Sorry, the branch associated with the purchase order (Branch ID: {purchaseOrder.BranchId}) does not have a registered warehouse in the system. Please create a warehouse for this branch first.");
                                ViewData["PurchaseOrderId"] = new SelectList(_context.PurchaseOrders, "Id", "Id", vendorInvoice.PurchaseOrderId);
                                return View(vendorInvoice);
                            }

                            if (purchaseOrder.PurchaseOrderItems != null)
                            {
                                foreach (var item in purchaseOrder.PurchaseOrderItems)
                                {
                                    var inventoryItem = await _context.BranchInventories
                                        .FirstOrDefaultAsync(bi => bi.WarehouseId == warehouse.Id && bi.ProductId == item.ProductId);

                                    if (inventoryItem != null)
                                    {
                                        inventoryItem.StockQuantity += item.Quantity; // زيادة الكمية بالمخزن
                                    }
                                    else
                                    {
                                        var newInventory = new BranchInventory
                                        {
                                            WarehouseId = warehouse.Id,
                                            ProductId = item.ProductId,
                                            StockQuantity = item.Quantity
                                        };
                                        _context.BranchInventories.Add(newInventory);
                                    }
                                }
                            }
                        }
                    }

                    _context.Update(existingInvoice);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!VendorInvoiceExists(vendorInvoice.Id))
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

            ViewData["PurchaseOrderId"] = new SelectList(_context.PurchaseOrders, "Id", "Id", vendorInvoice.PurchaseOrderId);
            return View(vendorInvoice);
        }

        // POST: VendorInvoices/MarkAsPaid/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAsPaid(int id)
        {
            var existingInvoice = await _context.VendorInvoices
                .Include(v => v.PurchaseOrder)
                    .ThenInclude(po => po.PurchaseOrderItems)
                .FirstOrDefaultAsync(v => v.Id == id);

            if (existingInvoice == null)
            {
                return NotFound();
            }

            string oldStatus = existingInvoice.Status ?? string.Empty;
            existingInvoice.Status = "Paid";

            bool isNowPaid = true;
            bool wasPaidBefore = string.Equals(oldStatus, "Paid", StringComparison.OrdinalIgnoreCase) || 
                            string.Equals(oldStatus, "paid", StringComparison.OrdinalIgnoreCase);

            if (isNowPaid && !wasPaidBefore)
            {
                var purchaseOrder = existingInvoice.PurchaseOrder;

                if (purchaseOrder != null)
                {
                    // 1. ندور على مستودع تابع للبرانش ده
                    var warehouse = await _context.Warehouses
                        .FirstOrDefaultAsync(w => w.BranchId == purchaseOrder.BranchId);

                    // 2. لو البرانش ملوش مستودع خالص، بننشئ له مستودع تلقائي فوراً عشان الكميات تدخل فيه!
                    if (warehouse == null)
                    {
                        warehouse = new Warehouse
                        {
                            Name = $"Warehouse - Branch {purchaseOrder.BranchId}",
                            BranchId = purchaseOrder.BranchId
                        };
                        _context.Warehouses.Add(warehouse);
                        await _context.SaveChangesAsync(); // حفظ عشان ناخد الـ ID بتاع المستودع الجديد
                    }

                    // 3. إضافة أو تحديث كميات المنتجات في مخزن هذا الفرع
                    if (purchaseOrder.PurchaseOrderItems != null && purchaseOrder.PurchaseOrderItems.Any())
                    {
                        foreach (var item in purchaseOrder.PurchaseOrderItems)
                        {
                            var inventoryItem = await _context.BranchInventories
                                .FirstOrDefaultAsync(bi => bi.WarehouseId == warehouse.Id && bi.ProductId == item.ProductId);

                            if (inventoryItem != null)
                            {
                                inventoryItem.StockQuantity += item.Quantity; // زيادة الكمية الحالية
                            }
                            else
                            {
                                var newInventory = new BranchInventory
                                {
                                    WarehouseId = warehouse.Id,
                                    ProductId = item.ProductId,
                                    StockQuantity = item.Quantity // إدراج الكمية الجديدة
                                };
                                _context.BranchInventories.Add(newInventory);
                            }
                        }
                    }
                }
            }

            _context.Update(existingInvoice);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Details), new { id = existingInvoice.Id });
        }

        // GET: VendorInvoices/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var vendorInvoice = await _context.VendorInvoices
                .Include(v => v.PurchaseOrder)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (vendorInvoice == null)
            {
                return NotFound();
            }

            return View(vendorInvoice);
        }

        // POST: VendorInvoices/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var vendorInvoice = await _context.VendorInvoices.FindAsync(id);
            if (vendorInvoice != null)
            {
                _context.VendorInvoices.Remove(vendorInvoice);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool VendorInvoiceExists(int id)
        {
            _ = id;
            return _context.VendorInvoices.Any(e => e.Id == id);
        }
    }
}
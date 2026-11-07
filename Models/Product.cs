using System;
using System.Collections.Generic;

namespace EnterpriseSystem.Models;

public partial class Product
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public string Sku { get; set; } = null!;

    public decimal UnitPrice { get; set; }

    // تعديل هذا السطر ليصبح قابل للقيم الفارغة
    public int? CategoryId { get; set; }

    public virtual ICollection<BranchInventory> BranchInventories { get; set; } = new List<BranchInventory>();

    // تعديل هذا السطر وإزالة null!
    public virtual Category? Category { get; set; }

    public virtual ICollection<CreditNoteItem> CreditNoteItems { get; set; } = new List<CreditNoteItem>();

    public virtual ICollection<InvoiceItem> InvoiceItems { get; set; } = new List<InvoiceItem>();

    public virtual ICollection<PurchaseOrderItem> PurchaseOrderItems { get; set; } = new List<PurchaseOrderItem>();

    public virtual ICollection<SalesOrderItem> SalesOrderItems { get; set; } = new List<SalesOrderItem>();
}
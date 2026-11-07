using System;
using System.Collections.Generic;

namespace EnterpriseSystem.Models;

public partial class VendorInvoice
{
    public int Id { get; set; }

    public int PurchaseOrderId { get; set; }

    public string InvoiceNumber { get; set; } = null!;

    public DateTime InvoiceDate { get; set; }

    public decimal TotalAmount { get; set; }

    public string Status { get; set; } = null!;

    // تعديل هذا السطر ليقبل القيمة الفارغة:
    public virtual PurchaseOrder? PurchaseOrder { get; set; }
}
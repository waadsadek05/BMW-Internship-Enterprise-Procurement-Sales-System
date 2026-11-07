using System;
using System.Collections.Generic;

namespace EnterpriseSystem.Models;

public partial class CreditNote
{
    public int Id { get; set; }

    public int SalesInvoiceId { get; set; }

    public DateTime ReturnDate { get; set; }

    public string? Reason { get; set; }

    public decimal RefundAmount { get; set; }

    public virtual ICollection<CreditNoteItem> CreditNoteItems { get; set; } = new List<CreditNoteItem>();

    // دي الخاصية اللي هنجيب منها رقم الفاتورة أوتوماتيك
    public virtual SalesInvoice SalesInvoice { get; set; } = null!;
}
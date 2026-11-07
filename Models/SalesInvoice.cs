using System;
using System.Collections.Generic;

namespace EnterpriseSystem.Models;

public partial class SalesInvoice
{
    public int Id { get; set; }

    public int SalesOrderId { get; set; }

    public string InvoiceNumber { get; set; } = null!;

    public DateTime InvoiceDate { get; set; }

    public decimal TotalAmount { get; set; }

    public virtual ICollection<CreditNote> CreditNotes { get; set; } = new List<CreditNote>();

    public virtual ICollection<InvoiceItem> InvoiceItems { get; set; } = new List<InvoiceItem>();

    public virtual SalesOrder SalesOrder { get; set; } = null!;
}

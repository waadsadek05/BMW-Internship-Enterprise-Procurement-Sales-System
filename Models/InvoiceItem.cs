using System;
using System.Collections.Generic;

namespace EnterpriseSystem.Models;

public partial class InvoiceItem
{
    public int Id { get; set; }

    public int SalesInvoiceId { get; set; }

    public int ProductId { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public virtual Product Product { get; set; } = null!;

    public virtual SalesInvoice SalesInvoice { get; set; } = null!;
}

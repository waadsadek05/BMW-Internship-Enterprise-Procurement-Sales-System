using System;
using System.Collections.Generic;

namespace EnterpriseSystem.Models;

public partial class CreditNoteItem
{
    public int Id { get; set; }

    public int CreditNoteId { get; set; }

    public int ProductId { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public virtual CreditNote CreditNote { get; set; } = null!;

    public virtual Product Product { get; set; } = null!;
}

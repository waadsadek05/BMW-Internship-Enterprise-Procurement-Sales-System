using System;
using System.Collections.Generic;

namespace EnterpriseSystem.Models;

public partial class UserRole
{
    public int Id { get; set; }

    public string UserId { get; set; } = null!;

    public string RoleId { get; set; } = null!;

    public virtual Role Role { get; set; } = null!;

    public virtual ApplicationUser User { get; set; } = null!;
}

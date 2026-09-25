using Microsoft.AspNetCore.Identity;

namespace WebhookRouter.Models;

public sealed class AppUser : IdentityUser<Guid>
{
    public bool IsEnabled { get; set; } = true;
    public bool AllowPasswordAuthentication { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public ICollection<Tenant> Tenants { get; set; } = [];
}

using Microsoft.AspNetCore.Identity;

namespace WebhookServer.Models;

public sealed class AppUser : IdentityUser<Guid>
{
    public ICollection<Tenant> Tenants { get; set; } = [];
}

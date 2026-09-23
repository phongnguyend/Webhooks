using Microsoft.AspNetCore.Identity;

namespace WebhookServer.Models;

public sealed class AppUser : IdentityUser<Guid>
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public ICollection<Tenant> Tenants { get; set; } = [];
}

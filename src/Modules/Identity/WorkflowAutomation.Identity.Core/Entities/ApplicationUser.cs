namespace WorkflowAutomation.Identity.Core.Entities;

using Microsoft.AspNetCore.Identity;

public class ApplicationUser : IdentityUser<Guid>
{
    public ApplicationUser()
    {
        Id = Guid.NewGuid();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    public ApplicationUser(string userName, string email) : this()
    {
        UserName = userName;
        Email = email;
    }
}

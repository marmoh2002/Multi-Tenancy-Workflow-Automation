namespace WorkflowAutomation.Identity.Core.Entities;

using Microsoft.AspNetCore.Identity;

public class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole()
    {
        Id = Guid.NewGuid();
    }

    public ApplicationRole(string roleName) : this()
    {
        Name = roleName;
    }
}

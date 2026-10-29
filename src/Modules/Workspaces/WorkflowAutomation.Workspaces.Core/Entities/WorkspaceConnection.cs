namespace WorkflowAutomation.Workspaces.Core.Entities;


using WorkflowAutomation.Shared.Kernel.Primitives;


public class WorkspaceConnection : AuditableEntity<Guid>, IMustHaveWorkspace
{
  public Guid WorkspaceId { get; private set; }
  public string Name { get; private set; } = string.Empty;
  public string Provider { get; private set; } = string.Empty;
  public string EncryptedCredentials { get; private set; } = string.Empty;
  public string? Description { get; private set; }
  public bool IsActive { get; private set; }

  public Guid CreatedByUserId { get; private set; }

  private WorkspaceConnection()
  {

  }

  private WorkspaceConnection(Guid id,
    Guid workspaceId,
    string name,
    string provider,
    string encryptedCredentials,
    Guid createdByUserId,
    string? description = null)
    : base(id)
  {
    WorkspaceId = workspaceId;
    Name = name;
    Provider = provider;
    EncryptedCredentials = encryptedCredentials;
    Description = description;
    IsActive = true;
    CreatedByUserId = createdByUserId;
  }
  public static WorkspaceConnection Create(Guid id, Guid workspaceId, string name, string provider, string encryptedCredentials, Guid createdByUserId, string? description = null)
  {
    if (id == Guid.Empty)
    {
      throw new ArgumentException("Connection ID cannot be empty.", nameof(id));
    }
    if (workspaceId == Guid.Empty)
    {
      throw new ArgumentException("Workspace ID cannot be empty.", nameof(workspaceId));
    }

    if (createdByUserId == Guid.Empty)
    {
      throw new ArgumentException("Creator user ID cannot be empty.", nameof(createdByUserId));
    }
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ArgumentException.ThrowIfNullOrWhiteSpace(provider);
    ArgumentException.ThrowIfNullOrWhiteSpace(encryptedCredentials);
    return new WorkspaceConnection(
        id,
        workspaceId,
        name.Trim(),
        provider.Trim(),
        encryptedCredentials,
        createdByUserId,
        description?.Trim());

  }
  public static WorkspaceConnection Create(Guid workspaceId, string name, string provider, string encryptedCredentials, Guid createdByUserId, string? description = null) => Create(Guid.NewGuid(), workspaceId, name, provider, encryptedCredentials, createdByUserId, description);
  public void UpdateCredentials(string encryptedCredentials)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(encryptedCredentials);
    EncryptedCredentials = encryptedCredentials;
  }

  public void UpdateDetails(string name, string? description)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    Name = name.Trim();
    Description = description?.Trim();
  }

  public void Activate()
  {
    IsActive = true;
  }
  public void Deactivate()
  {
    IsActive = false;
  }


}

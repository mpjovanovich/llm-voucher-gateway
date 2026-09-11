namespace LlmVoucherGateway.Domain.Abstractions;

public abstract class Entity
{
    protected Entity(Guid id)
    {
        Id = id;
    }

    protected Entity()
    {
        // Required by EF Core
    }

    public Guid Id { get; init; }
}
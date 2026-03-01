namespace Ingestion.Domain.Ports.Inbound;

public interface IProcessMessageUseCase<TDto>
{
    Task ExecuteAsync(TDto message, CancellationToken cancellationToken);
}

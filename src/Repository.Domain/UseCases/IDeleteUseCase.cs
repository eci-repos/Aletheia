using Aletheia.Foundation.Shared;
using Aletheia.Repository.Abstractions.Models;

namespace Aletheia.Repository.Domain.UseCases;

public interface IDeleteUseCase
{
    Task<Result> DeleteAsync(DeleteRequest request, CancellationToken cancellationToken = default);
}

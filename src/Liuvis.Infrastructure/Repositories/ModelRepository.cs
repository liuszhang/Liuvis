using Microsoft.EntityFrameworkCore;
using Liuvis.Core.Entities;
using Liuvis.Infrastructure.Persistence;

namespace Liuvis.Infrastructure.Repositories;

/// <summary>Repository for Model3D entity operations.</summary>
public class ModelRepository
{
    private readonly LiuvisDbContext _db;

    public ModelRepository(LiuvisDbContext db) => _db = db;

    public virtual async Task<Model3D?> GetByIdAsync(Guid modelId, CancellationToken ct = default)
        => await _db.Models
            .Include(m => m.Components)
            .FirstOrDefaultAsync(m => m.ModelId == modelId, ct);

    public virtual async Task<Model3D> CreateAsync(Model3D model, CancellationToken ct = default)
    {
        _db.Models.Add(model);
        await _db.SaveChangesAsync(ct);
        return model;
    }

    public virtual async Task UpdateAsync(Model3D model, CancellationToken ct = default)
    {
        _db.Models.Update(model);
        await _db.SaveChangesAsync(ct);
    }

    public virtual async Task<List<Model3D>> SearchByNameAsync(string query, int limit = 10, CancellationToken ct = default)
        => await _db.Models
            .Where(m => EF.Functions.ILike(m.Name, $"%{query}%") || EF.Functions.ILike(m.Description, $"%{query}%"))
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
}

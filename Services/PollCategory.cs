using Microsoft.EntityFrameworkCore;
using PollApi.Data;
using PollApi.DTOs.Request;
using PollApi.DTOs.Response;
using PollApi.Models;
using PollApi.Services.Interfaces;

namespace PollApi.Services
{
    public class PollCategoryService : IPollCategoryService
    {
        private readonly ApplicationDbContext _db;
        public PollCategoryService(ApplicationDbContext db) { _db = db; }

        public async Task<List<PollCategoryResponse>> GetAllAsync()
            => await _db.PollCategories
                        .Select(c => Map(c))
                        .ToListAsync();

        public async Task<PollCategoryResponse> GetByIdAsync(int id)
        {
            var c = await _db.PollCategories.FindAsync(id)
                    ?? throw new KeyNotFoundException("Category not found.");
            return Map(c);
        }

        public async Task<PollCategoryResponse> CreateAsync(CreatePollCategoryRequest req)
        {
            var c = new PollCategory
            {
                Name = req.Name,
                Description = req.Description,
                GoalkeeperOnly = req.GoalkeeperOnly
            };
            _db.PollCategories.Add(c);
            await _db.SaveChangesAsync();
            return Map(c);
        }

        public async Task<PollCategoryResponse> UpdateAsync(int id, CreatePollCategoryRequest req)
        {
            var c = await _db.PollCategories.FindAsync(id)
                    ?? throw new KeyNotFoundException("Category not found.");
            c.Name = req.Name;
            c.Description = req.Description;
            c.GoalkeeperOnly = req.GoalkeeperOnly;
            await _db.SaveChangesAsync();
            return Map(c);
        }

        public async Task SoftDeleteAsync(int id)
        {
            var c = await _db.PollCategories.FindAsync(id)
                    ?? throw new KeyNotFoundException("Category not found.");
            c.IsDeleted = true;
            await _db.SaveChangesAsync();
        }

        private static PollCategoryResponse Map(PollCategory c) => new()
        {
            Id = c.Id,
            Name = c.Name,
            Description = c.Description,
            GoalkeeperOnly = c.GoalkeeperOnly
        };
    }
}
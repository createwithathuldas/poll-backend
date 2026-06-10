using Microsoft.EntityFrameworkCore;
using PollApi.Models;

namespace PollApi.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        // ── DbSets ──────────────────────────────────────────────────────────────
        public DbSet<User> Users { get; set; }
        public DbSet<FifaTeams> FifaTeams { get; set; }
        public DbSet<TeamGoalkeeper> TeamGoalkeepers { get; set; }
        public DbSet<PollCategory> PollCategories { get; set; }
        public DbSet<Poll> Polls { get; set; }
        public DbSet<PollTeamOption> PollTeamOptions { get; set; }
        public DbSet<Vote> Votes { get; set; }
        public DbSet<ChatMessage> ChatMessages { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<KickLog> KickLogs { get; set; }

        protected override void OnModelCreating(ModelBuilder mb)
        {
            base.OnModelCreating(mb);

            // ── Vote: one vote per user per poll ────────────────────────────────
            mb.Entity<Vote>()
                .HasIndex(v => new { v.UserId, v.PollId })
                .IsUnique();

            // ── Vote → User ─────────────────────────────────────────────────────
            mb.Entity<Vote>()
                .HasOne(v => v.User)
                .WithMany(u => u.Votes)
                .HasForeignKey(v => v.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // ── Vote → Poll ─────────────────────────────────────────────────────
            mb.Entity<Vote>()
                .HasOne(v => v.Poll)
                .WithMany(p => p.Votes)
                .HasForeignKey(v => v.PollId)
                .OnDelete(DeleteBehavior.Restrict);

            // ── Vote → Team ─────────────────────────────────────────────────────
            mb.Entity<Vote>()
                .HasOne(v => v.Team)
                .WithMany(t => t.Votes)
                .HasForeignKey(v => v.TeamId)
                .OnDelete(DeleteBehavior.Restrict);

            // ── Vote → Goalkeeper (optional) ────────────────────────────────────
            mb.Entity<Vote>()
                .HasOne(v => v.Goalkeeper)
                .WithMany(g => g.Votes)
                .HasForeignKey(v => v.GoalkeeperId)
                .OnDelete(DeleteBehavior.Restrict);

            // ── KickLog → User / Admin (two FKs to same table) ─────────────────
            mb.Entity<KickLog>()
                .HasOne(k => k.User)
                .WithMany()
                .HasForeignKey(k => k.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            mb.Entity<KickLog>()
                .HasOne(k => k.Admin)
                .WithMany()
                .HasForeignKey(k => k.AdminId)
                .OnDelete(DeleteBehavior.Restrict);

            // ── Soft-delete global query filters ───────────────────────────────
            mb.Entity<User>().HasQueryFilter(e => !e.IsDeleted);
            mb.Entity<FifaTeams>().HasQueryFilter(e => !e.IsDeleted);
            mb.Entity<TeamGoalkeeper>().HasQueryFilter(e => !e.IsDeleted);
            mb.Entity<PollCategory>().HasQueryFilter(e => !e.IsDeleted);
            mb.Entity<Poll>().HasQueryFilter(e => !e.IsDeleted);
            mb.Entity<PollTeamOption>().HasQueryFilter(e => !e.IsDeleted);
            mb.Entity<ChatMessage>().HasQueryFilter(e => !e.IsDeleted);
            mb.Entity<Notification>().HasQueryFilter(e => !e.IsDeleted);

            // ── FifaTeams: Players stored as JSON ──────────────────────────────
            mb.Entity<FifaTeams>()
                .Property(t => t.Players)
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => System.Text.Json.JsonSerializer.Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null)!
                );

            // ── Seed: Admin user ────────────────────────────────────────────────
            mb.Entity<User>().HasData(new User
            {
                Id = 1,
                Name = "Admin",
                Email = "admin@fifapoll.com",
                Role = "Admin",
                Password = BCrypt.Net.BCrypt.HashPassword("Admin@1234"),
                IsDeleted = false,
                IsKicked = false
            });
        }
    }
}
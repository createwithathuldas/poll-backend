using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PollApi.Data;
using PollApi.DTOs.Request;
using PollApi.DTOs.Response;
using PollApi.Hubs;
using PollApi.Models;
using PollApi.Services.Interfaces;
using Microsoft.AspNetCore.SignalR;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace PollApi.Services
{
    // ── Auth ────────────────────────────────────────────────────────────────────

    public class AuthService : IAuthService
    {
        private readonly ApplicationDbContext _db;
        private readonly IConfiguration _cfg;

        public AuthService(ApplicationDbContext db, IConfiguration cfg)
        { _db = db; _cfg = cfg; }

        public async Task<AuthResponse> LoginAsync(LoginRequest req)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == req.Email)
                       ?? throw new UnauthorizedAccessException("Invalid credentials.");

            if (!BCrypt.Net.BCrypt.Verify(req.Password, user.Password))
                throw new UnauthorizedAccessException("Invalid credentials.");

            if (user.IsKicked)
                throw new UnauthorizedAccessException($"Account suspended: {user.KickReason}");

            return BuildAuth(user);
        }

        public async Task<AuthResponse> RegisterAsync(RegisterRequest req)
        {
            if (await _db.Users.AnyAsync(u => u.Email == req.Email))
                throw new InvalidOperationException("Email already registered.");

            var user = new User
            {
                Name = req.Name,
                Email = req.Email,
                Role = "User",
                Password = BCrypt.Net.BCrypt.HashPassword(req.Password)
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync();
            return BuildAuth(user);
        }

        public Task<AuthResponse> RefreshTokenAsync(string refreshToken) => throw new NotImplementedException("Implement Redis refresh token store.");
        public Task RevokeTokenAsync(string refreshToken) => throw new NotImplementedException();

        private AuthResponse BuildAuth(User user)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_cfg["Jwt:Secret"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Name),
                new Claim(ClaimTypes.Role, user.Role)
            };
            var token = new JwtSecurityToken(
                issuer: _cfg["Jwt:Issuer"],
                audience: _cfg["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddHours(8),
                signingCredentials: creds);

            return new AuthResponse
            {
                Token = new JwtSecurityTokenHandler().WriteToken(token),
                RefreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
                User = MapUser(user)
            };
        }

        private static UserResponse MapUser(User u) => new()
        {
            Id = u.Id, Name = u.Name, Email = u.Email,
            Role = u.Role, IsKicked = u.IsKicked, KickReason = u.KickReason
        };
    }

    // ── Team ────────────────────────────────────────────────────────────────────

    public class TeamService : ITeamService
    {
        private readonly ApplicationDbContext _db;
        public TeamService(ApplicationDbContext db) { _db = db; }

        public async Task<PagedResponse<TeamResponse>> GetAllAsync(int page, int size, string? confederation)
        {
            var q = _db.FifaTeams.Include(t => t.Goalkeepers).AsQueryable();
            if (confederation != null) q = q.Where(t => t.Confederation == confederation);
            var total = await q.CountAsync();
            var items = await q.OrderBy(t => t.FIFA_Ranking)
                               .Skip((page - 1) * size).Take(size)
                               .ToListAsync();
            return new PagedResponse<TeamResponse>
            {
                Items = items.Select(Map).ToList(),
                TotalCount = total, Page = page, PageSize = size
            };
        }

        public async Task<TeamResponse> GetByIdAsync(int id)
        {
            var t = await _db.FifaTeams.Include(t => t.Goalkeepers)
                             .FirstOrDefaultAsync(t => t.Id == id)
                    ?? throw new KeyNotFoundException("Team not found.");
            return Map(t);
        }

        public async Task<TeamResponse> CreateAsync(CreateTeamRequest req)
        {
            var t = new FifaTeams
            {
                TeamName = req.TeamName, Confederation = req.Confederation,
                FIFA_Ranking = req.FIFA_Ranking, Captain = req.Captain,
                TopScorer = req.TopScorer, Coach = req.Coach,
                Players = req.Players, WorldCupWins = req.WorldCupWins,
                WorldCupAppearances = req.WorldCupAppearances
            };
            _db.FifaTeams.Add(t);
            await _db.SaveChangesAsync();
            return Map(t);
        }

        public async Task<TeamResponse> UpdateAsync(int id, UpdateTeamRequest req)
        {
            var t = await _db.FifaTeams.FindAsync(id) ?? throw new KeyNotFoundException("Team not found.");
            t.TeamName = req.TeamName; t.Confederation = req.Confederation;
            t.FIFA_Ranking = req.FIFA_Ranking; t.Captain = req.Captain;
            t.TopScorer = req.TopScorer; t.Coach = req.Coach;
            t.Players = req.Players; t.WorldCupWins = req.WorldCupWins;
            t.WorldCupAppearances = req.WorldCupAppearances;
            await _db.SaveChangesAsync();
            return await GetByIdAsync(id);
        }

        public async Task SoftDeleteAsync(int id)
        {
            var t = await _db.FifaTeams.FindAsync(id) ?? throw new KeyNotFoundException();
            t.IsDeleted = true;
            await _db.SaveChangesAsync();
        }

        public async Task<GoalkeeperResponse> AddGoalkeeperAsync(CreateGoalkeeperRequest req)
        {
            var gk = new TeamGoalkeeper
            {
                TeamId = req.TeamId, Name = req.Name,
                PhotoUrl = req.PhotoUrl, JerseyNumber = req.JerseyNumber
            };
            _db.TeamGoalkeepers.Add(gk);
            await _db.SaveChangesAsync();
            var team = await _db.FifaTeams.FindAsync(req.TeamId);
            return MapGk(gk, team!.TeamName);
        }

        public async Task SoftDeleteGoalkeeperAsync(int id)
        {
            var gk = await _db.TeamGoalkeepers.FindAsync(id) ?? throw new KeyNotFoundException();
            gk.IsDeleted = true;
            await _db.SaveChangesAsync();
        }

        public async Task<List<GoalkeeperResponse>> GetGoalkeepersByTeamAsync(int teamId)
        {
            var team = await _db.FifaTeams.Include(t => t.Goalkeepers)
                                .FirstOrDefaultAsync(t => t.Id == teamId)
                       ?? throw new KeyNotFoundException("Team not found.");
            return team.Goalkeepers.Select(g => MapGk(g, team.TeamName)).ToList();
        }

        private static TeamResponse Map(FifaTeams t) => new()
        {
            Id = t.Id, TeamName = t.TeamName, Confederation = t.Confederation,
            FIFA_Ranking = t.FIFA_Ranking, Captain = t.Captain,
            TopScorer = t.TopScorer, Coach = t.Coach, Players = t.Players,
            WorldCupWins = t.WorldCupWins, WorldCupAppearances = t.WorldCupAppearances,
            Goalkeepers = t.Goalkeepers?.Select(g => MapGk(g, t.TeamName)).ToList() ?? new()
        };

        private static GoalkeeperResponse MapGk(TeamGoalkeeper g, string teamName) => new()
        {
            Id = g.Id, TeamId = g.TeamId, TeamName = teamName,
            Name = g.Name, PhotoUrl = g.PhotoUrl, JerseyNumber = g.JerseyNumber
        };
    }

    // ── Poll ────────────────────────────────────────────────────────────────────

    public class PollService : IPollService
    {
        private readonly ApplicationDbContext _db;
        private readonly IHubContext<PollHub> _hub;
        private readonly INotificationService _notify;

        public PollService(ApplicationDbContext db, IHubContext<PollHub> hub, INotificationService notify)
        { _db = db; _hub = hub; _notify = notify; }

        public async Task<PagedResponse<PollResponse>> GetAllAsync(int page, int size, PollStatus? status, int? catId, int uid)
        {
            var q = _db.Polls
                       .Include(p => p.Category)
                       .Include(p => p.TeamOptions).ThenInclude(o => o.Team).ThenInclude(t => t.Goalkeepers)
                       .Include(p => p.TeamOptions).ThenInclude(o => o.Goalkeeper)
                       .Include(p => p.Votes)
                       .AsQueryable();

            if (status.HasValue) q = q.Where(p => p.Status == status.Value);
            if (catId.HasValue) q = q.Where(p => p.CategoryId == catId.Value);

            var total = await q.CountAsync();
            var items = await q.OrderByDescending(p => p.CreatedAt)
                               .Skip((page - 1) * size).Take(size)
                               .ToListAsync();

            return new PagedResponse<PollResponse>
            {
                Items = items.Select(p => MapPoll(p, uid)).ToList(),
                TotalCount = total, Page = page, PageSize = size
            };
        }

        public async Task<PollResponse> GetByIdAsync(int id, int uid)
        {
            var p = await LoadPoll(id);
            return MapPoll(p, uid);
        }

        public async Task<PollResponse> CreateAsync(CreatePollRequest req)
        {
            var poll = new Poll
            {
                Title = req.Title, Description = req.Description,
                CategoryId = req.CategoryId, EndsAt = req.EndsAt,
                Status = PollStatus.Draft
            };
            _db.Polls.Add(poll);
            await _db.SaveChangesAsync();

            foreach (var opt in req.TeamOptions)
                _db.PollTeamOptions.Add(new PollTeamOption
                {
                    PollId = poll.Id, TeamId = opt.TeamId,
                    GoalkeeperId = opt.GoalkeeperId,
                    FanBannerUrl = opt.FanBannerUrl,
                    AnimationTheme = opt.AnimationTheme
                });

            await _db.SaveChangesAsync();
            return await GetByIdAsync(poll.Id, 0);
        }

        public async Task<PollResponse> UpdateAsync(int id, UpdatePollRequest req)
        {
            var p = await _db.Polls.FindAsync(id) ?? throw new KeyNotFoundException("Poll not found.");
            if (req.Title != null) p.Title = req.Title;
            if (req.Description != null) p.Description = req.Description;
            if (req.EndsAt.HasValue) p.EndsAt = req.EndsAt;
            await _db.SaveChangesAsync();
            return await GetByIdAsync(id, 0);
        }

        public async Task ChangeStatusAsync(int id, PollStatus status)
        {
            var p = await _db.Polls.FindAsync(id) ?? throw new KeyNotFoundException();
            p.Status = status;
            await _db.SaveChangesAsync();

            if (status == PollStatus.Active)
                await _notify.BroadcastAsync($"Poll Started: {p.Title}", "A new poll is now open for voting!", NotificationType.PollStarted, id);
        }

        public async Task<PollResultResponse> RevealResultAsync(int id)
        {
            var p = await LoadPoll(id);
            if (p.Status != PollStatus.Closed)
                throw new InvalidOperationException("Poll must be closed before revealing results.");

            p.ResultRevealed = true;
            p.ResultRevealedAt = DateTime.UtcNow;
            p.Status = PollStatus.ResultRevealed;
            await _db.SaveChangesAsync();

            var result = BuildResult(p);

            // Push via SignalR
            await _hub.Clients.All.SendAsync("ResultRevealed", result);

            // Notify all users
            await _notify.BroadcastAsync(
                $"Results Revealed: {p.Title}",
                $"Winner: {result.Winner?.Team?.TeamName ?? result.Winner?.Goalkeeper?.Name}",
                NotificationType.ResultRevealed, id);

            return result;
        }

        public async Task<PollResultResponse> GetResultAsync(int id)
        {
            var p = await LoadPoll(id);
            if (!p.ResultRevealed)
                throw new InvalidOperationException("Results not yet revealed.");
            return BuildResult(p);
        }

        public async Task SoftDeleteAsync(int id)
        {
            var p = await _db.Polls.FindAsync(id) ?? throw new KeyNotFoundException();
            p.IsDeleted = true;
            await _db.SaveChangesAsync();
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private async Task<Poll> LoadPoll(int id) =>
            await _db.Polls
                     .Include(p => p.Category)
                     .Include(p => p.TeamOptions).ThenInclude(o => o.Team).ThenInclude(t => t.Goalkeepers)
                     .Include(p => p.TeamOptions).ThenInclude(o => o.Goalkeeper)
                     .Include(p => p.Votes)
                     .FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new KeyNotFoundException("Poll not found.");

        private static PollResponse MapPoll(Poll p, int userId)
        {
            var total = p.Votes.Count;
            var revealed = p.ResultRevealed;
            return new PollResponse
            {
                Id = p.Id, Title = p.Title, Description = p.Description,
                Category = new PollCategoryResponse
                {
                    Id = p.Category.Id, Name = p.Category.Name,
                    Description = p.Category.Description,
                    GoalkeeperOnly = p.Category.GoalkeeperOnly
                },
                Status = p.Status, ResultRevealed = p.ResultRevealed,
                ResultRevealedAt = p.ResultRevealedAt,
                CreatedAt = p.CreatedAt, EndsAt = p.EndsAt,
                TotalVotes = total,
                HasVoted = p.Votes.Any(v => v.UserId == userId),
                TeamOptions = p.TeamOptions.Select(o =>
                {
                    var vc = p.Votes.Count(v => v.TeamId == o.TeamId &&
                                               (o.GoalkeeperId == null || v.GoalkeeperId == o.GoalkeeperId));
                    return new PollTeamOptionResponse
                    {
                        Id = o.Id,
                        Team = MapTeam(o.Team),
                        Goalkeeper = o.Goalkeeper == null ? null : MapGk(o.Goalkeeper, o.Team?.TeamName ?? ""),
                        FanBannerUrl = o.FanBannerUrl,
                        AnimationTheme = o.AnimationTheme,
                        VoteCount = revealed ? vc : 0,
                        VotePercent = revealed && total > 0 ? Math.Round((double)vc / total * 100, 1) : 0
                    };
                }).ToList()
            };
        }

        private static PollResultResponse BuildResult(Poll p)
        {
            var mapped = MapPoll(p, 0);
            var sorted = mapped.TeamOptions.OrderByDescending(o => o.VoteCount).ToList();
            return new PollResultResponse
            {
                PollId = p.Id, PollTitle = p.Title,
                Category = p.Category.Name,
                TotalVotes = mapped.TotalVotes,
                RevealedAt = p.ResultRevealedAt,
                Results = sorted,
                Winner = sorted.FirstOrDefault()
            };
        }

        private static TeamResponse MapTeam(FifaTeams t) => new()
        {
            Id = t.Id, TeamName = t.TeamName, Confederation = t.Confederation,
            FIFA_Ranking = t.FIFA_Ranking, Captain = t.Captain,
            TopScorer = t.TopScorer, Coach = t.Coach,
            Players = t.Players, WorldCupWins = t.WorldCupWins,
            WorldCupAppearances = t.WorldCupAppearances,
            Goalkeepers = t.Goalkeepers?.Select(g => MapGk(g, t.TeamName)).ToList() ?? new()
        };

        private static GoalkeeperResponse MapGk(TeamGoalkeeper g, string teamName) => new()
        {
            Id = g.Id, TeamId = g.TeamId, TeamName = teamName,
            Name = g.Name, PhotoUrl = g.PhotoUrl, JerseyNumber = g.JerseyNumber
        };
    }

    // ── Vote ────────────────────────────────────────────────────────────────────

    public class VoteService : IVoteService
    {
        private readonly ApplicationDbContext _db;
        public VoteService(ApplicationDbContext db) { _db = db; }

        public async Task<VoteResponse> CastVoteAsync(CastVoteRequest req, int userId)
        {
            // Hard control: one vote per user per poll
            if (await _db.Votes.AnyAsync(v => v.UserId == userId && v.PollId == req.PollId))
                throw new InvalidOperationException("You have already voted in this poll.");

            var poll = await _db.Polls.Include(p => p.Category)
                                .FirstOrDefaultAsync(p => p.Id == req.PollId)
                       ?? throw new KeyNotFoundException("Poll not found.");

            if (poll.Status != PollStatus.Active)
                throw new InvalidOperationException("Poll is not active.");

            if (poll.Category.GoalkeeperOnly && req.GoalkeeperId == null)
                throw new InvalidOperationException("This category requires selecting a goalkeeper.");

            var vote = new Vote
            {
                UserId = userId, PollId = req.PollId,
                TeamId = req.TeamId, GoalkeeperId = req.GoalkeeperId
            };
            _db.Votes.Add(vote);
            await _db.SaveChangesAsync();

            return await MapVote(vote);
        }

        public async Task<bool> HasVotedAsync(int pollId, int userId) =>
            await _db.Votes.AnyAsync(v => v.PollId == pollId && v.UserId == userId);

        public async Task<VoteResponse?> GetMyVoteAsync(int pollId, int userId)
        {
            var v = await _db.Votes.Include(v => v.Poll).Include(v => v.Team).Include(v => v.Goalkeeper)
                             .FirstOrDefaultAsync(v => v.PollId == pollId && v.UserId == userId);
            return v == null ? null : MapVoteSync(v);
        }

        public async Task<List<VoteResponse>> GetMyVotesAsync(int userId)
        {
            var votes = await _db.Votes.Include(v => v.Poll).Include(v => v.Team).Include(v => v.Goalkeeper)
                                  .Where(v => v.UserId == userId).ToListAsync();
            return votes.Select(MapVoteSync).ToList();
        }

        private Task<VoteResponse> MapVote(Vote v) => Task.FromResult(MapVoteSync(v));

        private static VoteResponse MapVoteSync(Vote v) => new()
        {
            Id = v.Id, PollId = v.PollId,
            PollTitle = v.Poll?.Title ?? "",
            TeamId = v.TeamId, TeamName = v.Team?.TeamName ?? "",
            GoalkeeperId = v.GoalkeeperId,
            GoalkeeperName = v.Goalkeeper?.Name,
            VotedAt = v.VotedAt
        };
    }

    // ── Chat ────────────────────────────────────────────────────────────────────

    public class ChatService : IChatService
    {
        private readonly ApplicationDbContext _db;
        public ChatService(ApplicationDbContext db) { _db = db; }

        public async Task<PagedResponse<ChatMessageResponse>> GetMessagesAsync(int? pollId, int page, int size)
        {
            var q = _db.ChatMessages.Include(m => m.User).AsQueryable();
            q = pollId.HasValue ? q.Where(m => m.PollId == pollId) : q.Where(m => m.PollId == null);
            var total = await q.CountAsync();
            var items = await q.OrderByDescending(m => m.SentAt)
                               .Skip((page - 1) * size).Take(size)
                               .ToListAsync();
            return new PagedResponse<ChatMessageResponse>
            {
                Items = items.Select(Map).ToList(), TotalCount = total, Page = page, PageSize = size
            };
        }

        public async Task<ChatMessageResponse> SaveMessageAsync(SendChatRequest req, int userId)
        {
            var msg = new ChatMessage
            {
                UserId = userId, PollId = req.PollId, Message = req.Message
            };
            _db.ChatMessages.Add(msg);
            await _db.SaveChangesAsync();
            await _db.Entry(msg).Reference(m => m.User).LoadAsync();
            return Map(msg);
        }

        public async Task DeleteMessageAsync(int messageId, int adminId, string reason)
        {
            var msg = await _db.ChatMessages.FindAsync(messageId)
                      ?? throw new KeyNotFoundException("Message not found.");
            msg.IsDeleted = true;
            msg.IsDeletedByAdmin = true;
            msg.DeleteReason = reason;
            await _db.SaveChangesAsync();
        }

        private static ChatMessageResponse Map(ChatMessage m) => new()
        {
            Id = m.Id, UserId = m.UserId, UserName = m.User?.Name ?? "",
            PollId = m.PollId, Message = m.IsDeletedByAdmin ? "[Message removed by admin]" : m.Message,
            SentAt = m.SentAt, IsDeletedByAdmin = m.IsDeletedByAdmin, DeleteReason = m.DeleteReason
        };
    }

    // ── Notification ────────────────────────────────────────────────────────────

    public class NotificationService : INotificationService
    {
        private readonly ApplicationDbContext _db;
        private readonly IHubContext<PollHub> _hub;
        public NotificationService(ApplicationDbContext db, IHubContext<PollHub> hub)
        { _db = db; _hub = hub; }

        public async Task<List<NotificationResponse>> GetMyNotificationsAsync(int userId)
        {
            return await _db.Notifications
                .Where(n => n.UserId == null || n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Select(n => Map(n))
                .ToListAsync();
        }

        public async Task MarkReadAsync(int id, int userId)
        {
            var n = await _db.Notifications.FindAsync(id) ?? throw new KeyNotFoundException();
            n.IsRead = true;
            await _db.SaveChangesAsync();
        }

        public async Task MarkAllReadAsync(int userId)
        {
            var notifs = await _db.Notifications
                .Where(n => (n.UserId == null || n.UserId == userId) && !n.IsRead)
                .ToListAsync();
            notifs.ForEach(n => n.IsRead = true);
            await _db.SaveChangesAsync();
        }

        public async Task<NotificationResponse> CreateAsync(CreateNotificationRequest req)
        {
            var n = new Notification
            {
                UserId = req.UserId, Title = req.Title, Body = req.Body,
                Type = req.Type, PollId = req.PollId
            };
            _db.Notifications.Add(n);
            await _db.SaveChangesAsync();

            var resp = Map(n);
            if (req.UserId.HasValue)
                await _hub.Clients.Group($"user-{req.UserId}").SendAsync("Notification", resp);
            else
                await _hub.Clients.All.SendAsync("Notification", resp);

            return resp;
        }

        public async Task BroadcastAsync(string title, string body, NotificationType type, int? pollId = null)
        {
            await CreateAsync(new CreateNotificationRequest
            {
                Title = title, Body = body, Type = type, PollId = pollId
            });
        }

        private static NotificationResponse Map(Notification n) => new()
        {
            Id = n.Id, Title = n.Title, Body = n.Body,
            Type = n.Type.ToString(), PollId = n.PollId,
            IsRead = n.IsRead, CreatedAt = n.CreatedAt
        };
    }

    // ── User ────────────────────────────────────────────────────────────────────

    public class UserService : IUserService
    {
        private readonly ApplicationDbContext _db;
        private readonly IHubContext<PollHub> _hub;
        public UserService(ApplicationDbContext db, IHubContext<PollHub> hub)
        { _db = db; _hub = hub; }

        public async Task<PagedResponse<UserResponse>> GetAllAsync(int page, int size)
        {
            var total = await _db.Users.CountAsync();
            var items = await _db.Users.OrderBy(u => u.Name)
                                  .Skip((page - 1) * size).Take(size)
                                  .ToListAsync();
            return new PagedResponse<UserResponse>
            {
                Items = items.Select(Map).ToList(), TotalCount = total, Page = page, PageSize = size
            };
        }

        public async Task<UserResponse> GetByIdAsync(int id) =>
            Map(await _db.Users.FindAsync(id) ?? throw new KeyNotFoundException("User not found."));

        public async Task SoftDeleteAsync(int id)
        {
            var u = await _db.Users.FindAsync(id) ?? throw new KeyNotFoundException();
            u.IsDeleted = true;
            await _db.SaveChangesAsync();
        }

        public async Task<UserResponse> UpdateAsync(int id, RegisterRequest req)
        {
            var u = await _db.Users.FindAsync(id) ?? throw new KeyNotFoundException();
            u.Name = req.Name; u.Email = req.Email;
            if (!string.IsNullOrEmpty(req.Password))
                u.Password = BCrypt.Net.BCrypt.HashPassword(req.Password);
            await _db.SaveChangesAsync();
            return Map(u);
        }

        public async Task KickAsync(KickUserRequest req, int adminId)
        {
            var u = await _db.Users.FindAsync(req.UserId) ?? throw new KeyNotFoundException();
            u.IsKicked = true;
            u.KickReason = req.Reason;
            u.KickedAt = DateTime.UtcNow;

            _db.KickLogs.Add(new KickLog
            {
                UserId = req.UserId, AdminId = adminId, Reason = req.Reason
            });

            await _db.SaveChangesAsync();

            // Force-disconnect via SignalR
            await _hub.Clients.Group($"user-{req.UserId}").SendAsync("Kicked", new
            {
                Message = "You have been removed by the admin.",
                Reason = req.Reason
            });
        }

        public async Task<List<KickLogResponse>> GetKickLogsAsync()
        {
            return await _db.KickLogs
                .Include(k => k.User)
                .OrderByDescending(k => k.KickedAt)
                .Select(k => new KickLogResponse
                {
                    Id = k.Id,
                    User = Map(k.User),
                    Reason = k.Reason,
                    KickedAt = k.KickedAt
                })
                .ToListAsync();
        }

        private static UserResponse Map(User u) => new()
        {
            Id = u.Id, Name = u.Name, Email = u.Email,
            Role = u.Role, IsKicked = u.IsKicked, KickReason = u.KickReason
        };
    }
}
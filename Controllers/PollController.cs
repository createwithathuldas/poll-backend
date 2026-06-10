using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PollApi.Data;
using PollApi.DTOs.Request;
using PollApi.DTOs.Response;
using PollApi.Hubs;
using PollApi.Models;
using System.Security.Claims;

namespace PollApi.Controllers
{
    [ApiController]
    [Route("api/polls")]
    [Produces("application/json")]
    [Authorize]                            // all routes need auth unless [AllowAnonymous]
    public class PollController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IHubContext<PollHub> _hub;

        public PollController(ApplicationDbContext db, IHubContext<PollHub> hub)
        {
            _db  = db;
            _hub = hub;
        }

        private int    CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        private string CurrentRole   => User.FindFirstValue(ClaimTypes.Role) ?? "User";

        // ════════════════════════════════════════════════════════════════════════
        //  CLUBS (FIFA Teams) — Admin only
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// [Admin] Add a new club / FIFA team.
        /// Players list stored as JSON. Goalkeepers added separately via /goalkeepers.
        /// </summary>
        /// <remarks>
        /// POST /api/polls/clubs
        ///
        ///     {
        ///       "teamName": "Brazil",
        ///       "confederation": "CONMEBOL",
        ///       "FIFA_Ranking": 1,
        ///       "captain": "Marquinhos",
        ///       "topScorer": "Neymar",
        ///       "coach": "Dorival Júnior",
        ///       "players": ["Alisson","Marquinhos","Vinicius Jr"],
        ///       "worldCupWins": 5,
        ///       "worldCupAppearances": 22
        ///     }
        /// </remarks>
        [HttpPost("clubs")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(typeof(ApiResponse<TeamResponse>), 201)]
        public async Task<IActionResult> AddClub([FromBody] CreateTeamRequest req)
        {
            if (!ModelState.IsValid)
                return BadRequest(Fail(GetModelErrors()));

            var team = new FifaTeams
            {
                TeamName            = req.TeamName,
                Confederation       = req.Confederation,
                FIFA_Ranking        = req.FIFA_Ranking,
                Captain             = req.Captain,
                TopScorer           = req.TopScorer,
                Coach               = req.Coach,
                Players             = req.Players,
                WorldCupWins        = req.WorldCupWins,
                WorldCupAppearances = req.WorldCupAppearances
            };

            _db.FifaTeams.Add(team);
            await _db.SaveChangesAsync();

            // Notify all connected clients → new club added
            await _hub.Clients.All.SendAsync("ClubAdded", MapTeam(team));

            return CreatedAtAction(nameof(GetClub), new { id = team.Id },
                Success(MapTeam(team), "Club added successfully."));
        }

        /// <summary>[Admin] Update existing club details.</summary>
        [HttpPut("clubs/{id:int}")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(typeof(ApiResponse<TeamResponse>), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> UpdateClub(int id, [FromBody] UpdateTeamRequest req)
        {
            var team = await _db.FifaTeams.Include(t => t.Goalkeepers)
                                          .FirstOrDefaultAsync(t => t.Id == id);
            if (team == null) return NotFound(Fail("Club not found."));

            team.TeamName            = req.TeamName;
            team.Confederation       = req.Confederation;
            team.FIFA_Ranking        = req.FIFA_Ranking;
            team.Captain             = req.Captain;
            team.TopScorer           = req.TopScorer;
            team.Coach               = req.Coach;
            team.Players             = req.Players;
            team.WorldCupWins        = req.WorldCupWins;
            team.WorldCupAppearances = req.WorldCupAppearances;

            await _db.SaveChangesAsync();
            return Ok(Success(MapTeam(team)));
        }

        /// <summary>[Admin] Soft-delete a club.</summary>
        [HttpDelete("clubs/{id:int}")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(typeof(ApiResponse<object>), 200)]
        public async Task<IActionResult> DeleteClub(int id)
        {
            var team = await _db.FifaTeams.FindAsync(id);
            if (team == null) return NotFound(Fail("Club not found."));
            team.IsDeleted = true;
            await _db.SaveChangesAsync();
            return Ok(Success<object>(null!, "Club deleted."));
        }

        /// <summary>[Public] Get all clubs (paged). Optional confederation filter.</summary>
        [HttpGet("clubs")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(ApiResponse<PagedResponse<TeamResponse>>), 200)]
        public async Task<IActionResult> GetClubs(
            [FromQuery] int     page          = 1,
            [FromQuery] int     size          = 20,
            [FromQuery] string? confederation = null)
        {
            var q = _db.FifaTeams.Include(t => t.Goalkeepers).AsQueryable();
            if (confederation != null)
                q = q.Where(t => t.Confederation == confederation);

            var total = await q.CountAsync();
            var items = await q.OrderBy(t => t.FIFA_Ranking)
                               .Skip((page - 1) * size).Take(size)
                               .ToListAsync();

            return Ok(Success(new PagedResponse<TeamResponse>
            {
                Items      = items.Select(MapTeam).ToList(),
                TotalCount = total,
                Page       = page,
                PageSize   = size
            }));
        }

        /// <summary>[Public] Get single club with goalkeepers.</summary>
        [HttpGet("clubs/{id:int}")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(ApiResponse<TeamResponse>), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetClub(int id)
        {
            var team = await _db.FifaTeams.Include(t => t.Goalkeepers)
                                          .FirstOrDefaultAsync(t => t.Id == id);
            if (team == null) return NotFound(Fail("Club not found."));
            return Ok(Success(MapTeam(team)));
        }

        // ── Goalkeepers ──────────────────────────────────────────────────────────

        /// <summary>
        /// [Admin] Add goalkeeper to a club.
        /// Required for polls in GoalkeeperOnly categories (e.g. Golden Glove).
        /// </summary>
        [HttpPost("clubs/goalkeepers")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(typeof(ApiResponse<GoalkeeperResponse>), 201)]
        public async Task<IActionResult> AddGoalkeeper([FromBody] CreateGoalkeeperRequest req)
        {
            var team = await _db.FifaTeams.FindAsync(req.TeamId);
            if (team == null) return NotFound(Fail("Club not found."));

            var gk = new TeamGoalkeeper
            {
                TeamId       = req.TeamId,
                Name         = req.Name,
                PhotoUrl     = req.PhotoUrl,
                JerseyNumber = req.JerseyNumber
            };
            _db.TeamGoalkeepers.Add(gk);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetClub), new { id = req.TeamId },
                Success(MapGk(gk, team.TeamName), "Goalkeeper added."));
        }

        /// <summary>[Admin] Soft-delete goalkeeper.</summary>
        [HttpDelete("clubs/goalkeepers/{id:int}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteGoalkeeper(int id)
        {
            var gk = await _db.TeamGoalkeepers.FindAsync(id);
            if (gk == null) return NotFound(Fail("Goalkeeper not found."));
            gk.IsDeleted = true;
            await _db.SaveChangesAsync();
            return Ok(Success<object>(null!, "Goalkeeper removed."));
        }

        // ════════════════════════════════════════════════════════════════════════
        //  POLL CATEGORIES — Admin only
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// [Admin] Create a poll category.
        /// Set GoalkeeperOnly = true for categories like "Golden Glove" where
        /// each club's goalkeeper is the voting target, not the club itself.
        /// </summary>
        /// <remarks>
        /// POST /api/polls/categories
        ///
        ///     {
        ///       "name": "Golden Glove",
        ///       "description": "Vote for best goalkeeper of the tournament",
        ///       "goalkeeperOnly": true
        ///     }
        /// </remarks>
        [HttpPost("categories")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(typeof(ApiResponse<PollCategoryResponse>), 201)]
        public async Task<IActionResult> CreateCategory([FromBody] CreatePollCategoryRequest req)
        {
            if (!ModelState.IsValid) return BadRequest(Fail(GetModelErrors()));

            var cat = new PollCategory
            {
                Name           = req.Name,
                Description    = req.Description,
                GoalkeeperOnly = req.GoalkeeperOnly
            };
            _db.PollCategories.Add(cat);
            await _db.SaveChangesAsync();
            return CreatedAtAction(nameof(GetCategories), null, Success(MapCategory(cat)));
        }

        /// <summary>[Public] Get all poll categories.</summary>
        [HttpGet("categories")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(ApiResponse<List<PollCategoryResponse>>), 200)]
        public async Task<IActionResult> GetCategories()
        {
            var cats = await _db.PollCategories.ToListAsync();
            return Ok(Success(cats.Select(MapCategory).ToList()));
        }

        /// <summary>[Admin] Update category.</summary>
        [HttpPut("categories/{id:int}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateCategory(int id, [FromBody] CreatePollCategoryRequest req)
        {
            var cat = await _db.PollCategories.FindAsync(id);
            if (cat == null) return NotFound(Fail("Category not found."));
            cat.Name           = req.Name;
            cat.Description    = req.Description;
            cat.GoalkeeperOnly = req.GoalkeeperOnly;
            await _db.SaveChangesAsync();
            return Ok(Success(MapCategory(cat)));
        }

        /// <summary>[Admin] Soft-delete category.</summary>
        [HttpDelete("categories/{id:int}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteCategory(int id)
        {
            var cat = await _db.PollCategories.FindAsync(id);
            if (cat == null) return NotFound(Fail("Category not found."));
            cat.IsDeleted = true;
            await _db.SaveChangesAsync();
            return Ok(Success<object>(null!, "Category deleted."));
        }

        // ════════════════════════════════════════════════════════════════════════
        //  POLLS — Admin creates, Users view
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// [Admin] Create a poll with team options (and goalkeeper options for GK categories).
        /// Poll starts in Draft status. Use /status to activate.
        /// </summary>
        /// <remarks>
        /// POST /api/polls
        ///
        ///     {
        ///       "title": "Golden Glove - World Cup 2026",
        ///       "description": "Vote for best keeper",
        ///       "categoryId": 3,
        ///       "endsAt": "2026-07-15T23:59:00Z",
        ///       "teamOptions": [
        ///         { "teamId": 1, "goalkeeperId": 5, "fanBannerUrl": "https://cdn.../brazil.gif", "animationTheme": "confetti-green" },
        ///         { "teamId": 2, "goalkeeperId": 8, "fanBannerUrl": "https://cdn.../france.gif", "animationTheme": "fire-blue" }
        ///       ]
        ///     }
        /// </remarks>
        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(typeof(ApiResponse<PollResponse>), 201)]
        public async Task<IActionResult> CreatePoll([FromBody] CreatePollRequest req)
        {
            if (!ModelState.IsValid) return BadRequest(Fail(GetModelErrors()));

            var category = await _db.PollCategories.FindAsync(req.CategoryId);
            if (category == null) return BadRequest(Fail("Poll category not found."));

            // Validate: GoalkeeperOnly categories require GoalkeeperId on every option
            if (category.GoalkeeperOnly && req.TeamOptions.Any(o => o.GoalkeeperId == null))
                return BadRequest(Fail($"Category '{category.Name}' requires a GoalkeeperId for every team option."));

            var poll = new Poll
            {
                Title       = req.Title,
                Description = req.Description,
                CategoryId  = req.CategoryId,
                EndsAt      = req.EndsAt,
                Status      = PollStatus.Draft,
                CreatedAt   = DateTime.UtcNow
            };

            _db.Polls.Add(poll);
            await _db.SaveChangesAsync();

            foreach (var opt in req.TeamOptions)
            {
                _db.PollTeamOptions.Add(new PollTeamOption
                {
                    PollId          = poll.Id,
                    TeamId          = opt.TeamId,
                    GoalkeeperId    = opt.GoalkeeperId,
                    FanBannerUrl    = opt.FanBannerUrl,
                    AnimationTheme  = opt.AnimationTheme
                });
            }

            await _db.SaveChangesAsync();

            var created = await LoadFullPoll(poll.Id);
            return CreatedAtAction(nameof(GetPoll), new { id = poll.Id },
                Success(MapPoll(created!, CurrentUserId), "Poll created."));
        }

        /// <summary>
        /// [Admin] Change poll status.
        /// Flow: Draft → Active → Closed → (RevealResult endpoint marks ResultRevealed)
        /// Activating broadcasts PollStarted notification to all users.
        /// </summary>
        /// <remarks>
        /// PATCH /api/polls/{id}/status
        ///
        ///     { "status": 1 }   // 0=Draft, 1=Active, 2=Closed
        /// </remarks>
        [HttpPatch("{id:int}/status")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(typeof(ApiResponse<object>), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> ChangePollStatus(int id, [FromBody] ChangePollStatusRequest req)
        {
            var poll = await _db.Polls.FindAsync(id);
            if (poll == null) return NotFound(Fail("Poll not found."));

            poll.Status = req.Status;
            await _db.SaveChangesAsync();

            // Broadcast to all clients when poll goes live
            if (req.Status == PollStatus.Active)
            {
                await _hub.Clients.All.SendAsync("PollStarted", new
                {
                    PollId  = poll.Id,
                    Title   = poll.Title,
                    Message = $"🔥 New poll is now live: {poll.Title}"
                });

                // Persist notification for all users
                _db.Notifications.Add(new Notification
                {
                    Title     = "New Poll Started!",
                    Body      = $"Vote now: {poll.Title}",
                    Type      = NotificationType.PollStarted,
                    PollId    = poll.Id,
                    CreatedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();
            }

            return Ok(Success<object>(null!, $"Poll status updated to {req.Status}."));
        }

        /// <summary>[Admin] Update poll metadata (title, description, end date).</summary>
        [HttpPut("{id:int}")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(typeof(ApiResponse<PollResponse>), 200)]
        public async Task<IActionResult> UpdatePoll(int id, [FromBody] UpdatePollRequest req)
        {
            var poll = await _db.Polls.FindAsync(id);
            if (poll == null) return NotFound(Fail("Poll not found."));

            if (req.Title != null)       poll.Title       = req.Title;
            if (req.Description != null) poll.Description = req.Description;
            if (req.EndsAt.HasValue)     poll.EndsAt      = req.EndsAt;

            await _db.SaveChangesAsync();

            var updated = await LoadFullPoll(id);
            return Ok(Success(MapPoll(updated!, CurrentUserId)));
        }

        /// <summary>[Admin] Soft-delete poll.</summary>
        [HttpDelete("{id:int}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeletePoll(int id)
        {
            var poll = await _db.Polls.FindAsync(id);
            if (poll == null) return NotFound(Fail("Poll not found."));
            poll.IsDeleted = true;
            await _db.SaveChangesAsync();
            return Ok(Success<object>(null!, "Poll deleted."));
        }

        /// <summary>
        /// [Public] Get all polls — paged, filterable by status and/or category.
        /// Vote counts are hidden (0) until admin reveals results.
        /// Authenticated users get HasVoted flag per poll.
        /// </summary>
        [HttpGet]
        [AllowAnonymous]
        [ProducesResponseType(typeof(ApiResponse<PagedResponse<PollResponse>>), 200)]
        public async Task<IActionResult> GetPolls(
            [FromQuery] int         page       = 1,
            [FromQuery] int         size       = 10,
            [FromQuery] PollStatus? status     = null,
            [FromQuery] int?        categoryId = null)
        {
            var uid = User.Identity?.IsAuthenticated == true ? CurrentUserId : 0;

            var q = _db.Polls
                       .Include(p => p.Category)
                       .Include(p => p.TeamOptions).ThenInclude(o => o.Team).ThenInclude(t => t.Goalkeepers)
                       .Include(p => p.TeamOptions).ThenInclude(o => o.Goalkeeper)
                       .Include(p => p.Votes)
                       .AsQueryable();

            if (status.HasValue)   q = q.Where(p => p.Status == status.Value);
            if (categoryId.HasValue) q = q.Where(p => p.CategoryId == categoryId.Value);

            var total = await q.CountAsync();
            var items = await q.OrderByDescending(p => p.CreatedAt)
                               .Skip((page - 1) * size).Take(size)
                               .ToListAsync();

            return Ok(Success(new PagedResponse<PollResponse>
            {
                Items      = items.Select(p => MapPoll(p, uid)).ToList(),
                TotalCount = total,
                Page       = page,
                PageSize   = size
            }));
        }

        /// <summary>
        /// [Public] Get single poll by ID.
        /// Vote counts hidden until ResultRevealed = true.
        /// </summary>
        [HttpGet("{id:int}")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(ApiResponse<PollResponse>), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetPoll(int id)
        {
            var poll = await LoadFullPoll(id);
            if (poll == null) return NotFound(Fail("Poll not found."));
            var uid = User.Identity?.IsAuthenticated == true ? CurrentUserId : 0;
            return Ok(Success(MapPoll(poll, uid)));
        }

        // ════════════════════════════════════════════════════════════════════════
        //  VOTING — Users only
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// [User] Cast a vote.
        ///
        /// Hard rules enforced:
        /// 1. DB unique index on (UserId, PollId) — one vote per user per poll, database-level
        /// 2. Service-level AnyAsync check before insert → friendly error
        /// 3. Poll must be Active
        /// 4. GoalkeeperOnly category requires GoalkeeperId
        /// 5. Kicked users cannot vote
        ///
        /// Voted team option's fan banner animation is triggered on frontend via SignalR VoteCast event.
        /// </summary>
        /// <remarks>
        /// POST /api/polls/vote
        ///
        ///     {
        ///       "pollId": 1,
        ///       "teamId": 3,
        ///       "goalkeeperId": null     // required if category.GoalkeeperOnly = true
        ///     }
        /// </remarks>
        [HttpPost("vote")]
        [Authorize(Roles = "User")]
        [ProducesResponseType(typeof(ApiResponse<VoteResponse>), 200)]
        [ProducesResponseType(typeof(ApiResponse<object>), 400)]
        [ProducesResponseType(typeof(ApiResponse<object>), 409)]
        public async Task<IActionResult> CastVote([FromBody] CastVoteRequest req)
        {
            if (!ModelState.IsValid) return BadRequest(Fail(GetModelErrors()));

            var user = await _db.Users.FindAsync(CurrentUserId);
            if (user == null) return Unauthorized(Fail("User not found."));
            if (user.IsKicked) return Unauthorized(Fail($"Account suspended. Reason: {user.KickReason}"));

            // Hard control 1: DB + app-level one-vote enforcement
            var alreadyVoted = await _db.Votes
                .AnyAsync(v => v.UserId == CurrentUserId && v.PollId == req.PollId);
            if (alreadyVoted)
                return Conflict(Fail("You have already voted in this poll. One vote per user is strictly enforced."));

            var poll = await _db.Polls.Include(p => p.Category)
                                      .FirstOrDefaultAsync(p => p.Id == req.PollId);
            if (poll == null) return NotFound(Fail("Poll not found."));

            // Hard control 2: poll must be active
            if (poll.Status != PollStatus.Active)
                return BadRequest(Fail($"Voting is not open. Poll status: {poll.Status}."));

            // Hard control 3: GoalkeeperOnly requires GK selection
            if (poll.Category.GoalkeeperOnly && req.GoalkeeperId == null)
                return BadRequest(Fail($"Category '{poll.Category.Name}' requires selecting a goalkeeper."));

            var vote = new Vote
            {
                UserId       = CurrentUserId,
                PollId       = req.PollId,
                TeamId       = req.TeamId,
                GoalkeeperId = req.GoalkeeperId,
                VotedAt      = DateTime.UtcNow
            };

            _db.Votes.Add(vote);
            await _db.SaveChangesAsync();

            // Reload with nav props for response
            await _db.Entry(vote).Reference(v => v.Poll).LoadAsync();
            await _db.Entry(vote).Reference(v => v.Team).LoadAsync();
            if (vote.GoalkeeperId.HasValue)
                await _db.Entry(vote).Reference(v => v.Goalkeeper).LoadAsync();

            // Push live vote count update to all in poll room (counts still hidden from users)
            var voteCount = await _db.Votes.CountAsync(v => v.PollId == req.PollId);
            await _hub.Clients.Group($"poll-{req.PollId}").SendAsync("VoteCast", new
            {
                PollId     = req.PollId,
                TotalVotes = voteCount,
                TeamId     = req.TeamId,
                AnimationTheme = await _db.PollTeamOptions
                    .Where(o => o.PollId == req.PollId && o.TeamId == req.TeamId)
                    .Select(o => o.AnimationTheme)
                    .FirstOrDefaultAsync()
            });

            return Ok(Success(new VoteResponse
            {
                Id             = vote.Id,
                PollId         = vote.PollId,
                PollTitle      = vote.Poll?.Title ?? "",
                TeamId         = vote.TeamId,
                TeamName       = vote.Team?.TeamName ?? "",
                GoalkeeperId   = vote.GoalkeeperId,
                GoalkeeperName = vote.Goalkeeper?.Name,
                VotedAt        = vote.VotedAt
            }, "Vote cast successfully."));
        }

        /// <summary>[User] Check if current user has voted in a poll.</summary>
        [HttpGet("{pollId:int}/has-voted")]
        [ProducesResponseType(typeof(ApiResponse<object>), 200)]
        public async Task<IActionResult> HasVoted(int pollId)
        {
            var voted = await _db.Votes
                .AnyAsync(v => v.UserId == CurrentUserId && v.PollId == pollId);
            return Ok(Success<object>(new { HasVoted = voted, PollId = pollId }));
        }

        /// <summary>[User] Get my vote for a specific poll.</summary>
        [HttpGet("{pollId:int}/my-vote")]
        public async Task<IActionResult> GetMyVote(int pollId)
        {
            var vote = await _db.Votes
                .Include(v => v.Poll)
                .Include(v => v.Team)
                .Include(v => v.Goalkeeper)
                .FirstOrDefaultAsync(v => v.UserId == CurrentUserId && v.PollId == pollId);

            if (vote == null) return NotFound(Fail("No vote found for this poll."));

            return Ok(Success(new VoteResponse
            {
                Id             = vote.Id,
                PollId         = vote.PollId,
                PollTitle      = vote.Poll?.Title ?? "",
                TeamId         = vote.TeamId,
                TeamName       = vote.Team?.TeamName ?? "",
                GoalkeeperId   = vote.GoalkeeperId,
                GoalkeeperName = vote.Goalkeeper?.Name,
                VotedAt        = vote.VotedAt
            }));
        }

        /// <summary>[User] Get all my votes across all polls.</summary>
        [HttpGet("my-votes")]
        public async Task<IActionResult> GetMyVotes()
        {
            var votes = await _db.Votes
                .Include(v => v.Poll)
                .Include(v => v.Team)
                .Include(v => v.Goalkeeper)
                .Where(v => v.UserId == CurrentUserId)
                .OrderByDescending(v => v.VotedAt)
                .ToListAsync();

            return Ok(Success(votes.Select(v => new VoteResponse
            {
                Id             = v.Id,
                PollId         = v.PollId,
                PollTitle      = v.Poll?.Title ?? "",
                TeamId         = v.TeamId,
                TeamName       = v.Team?.TeamName ?? "",
                GoalkeeperId   = v.GoalkeeperId,
                GoalkeeperName = v.Goalkeeper?.Name,
                VotedAt        = v.VotedAt
            }).ToList()));
        }

        // ════════════════════════════════════════════════════════════════════════
        //  RESULTS — Admin reveals, then public
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// [Admin] Reveal poll results.
        /// Poll must be Closed first. Sets ResultRevealed = true and broadcasts
        /// full result breakdown via SignalR to all connected clients.
        /// Triggers a push notification to all users.
        /// </summary>
        /// <remarks>
        /// POST /api/polls/{id}/reveal-results
        /// No body required.
        /// </remarks>
        [HttpPost("{id:int}/reveal-results")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(typeof(ApiResponse<PollResultResponse>), 200)]
        [ProducesResponseType(typeof(ApiResponse<object>), 400)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> RevealResults(int id)
        {
            var poll = await LoadFullPoll(id);
            if (poll == null) return NotFound(Fail("Poll not found."));

            if (poll.Status != PollStatus.Closed)
                return BadRequest(Fail($"Poll must be Closed before revealing results. Current status: {poll.Status}."));

            poll.ResultRevealed   = true;
            poll.ResultRevealedAt = DateTime.UtcNow;
            poll.Status           = PollStatus.ResultRevealed;
            await _db.SaveChangesAsync();

            var result = BuildResult(poll);

            // 1️⃣  Broadcast full results to all connected SignalR clients
            await _hub.Clients.All.SendAsync("ResultRevealed", result);

            // 2️⃣  Push persistent notification to all users
            var winnerName = result.Winner?.Goalkeeper?.Name
                          ?? result.Winner?.Team?.TeamName
                          ?? "Unknown";

            _db.Notifications.Add(new Notification
            {
                Title     = $"🏆 Results Revealed: {poll.Title}",
                Body      = $"Winner: {winnerName} with {result.Winner?.VoteCount} votes!",
                Type      = NotificationType.ResultRevealed,
                PollId    = poll.Id,
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            return Ok(Success(result, "Results revealed and broadcast to all users."));
        }

        /// <summary>
        /// [Public] Get poll results.
        /// Returns 403 if admin hasn't revealed results yet.
        /// </summary>
        [HttpGet("{id:int}/results")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(ApiResponse<PollResultResponse>), 200)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetResults(int id)
        {
            var poll = await LoadFullPoll(id);
            if (poll == null) return NotFound(Fail("Poll not found."));

            if (!poll.ResultRevealed)
                return StatusCode(403, Fail("Results have not been revealed yet. Stay tuned!"));

            return Ok(Success(BuildResult(poll)));
        }

        // ════════════════════════════════════════════════════════════════════════
        //  KICK — Admin only
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// [Admin] Kick a user.
        /// Marks user IsKicked = true, records reason, broadcasts Kicked event
        /// to that user's SignalR group so the frontend can force-disconnect them.
        /// Kicked users cannot vote or chat.
        /// </summary>
        /// <remarks>
        /// POST /api/polls/users/kick
        ///
        ///     {
        ///       "userId": 42,
        ///       "reason": "Spamming in live chat"
        ///     }
        /// </remarks>
        [HttpPost("users/kick")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(typeof(ApiResponse<object>), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> KickUser([FromBody] KickUserRequest req)
        {
            var user = await _db.Users.FindAsync(req.UserId);
            if (user == null) return NotFound(Fail("User not found."));
            if (user.Role == "Admin") return BadRequest(Fail("Cannot kick an admin account."));

            user.IsKicked   = true;
            user.KickReason = req.Reason;
            user.KickedAt   = DateTime.UtcNow;

            _db.KickLogs.Add(new KickLog
            {
                UserId    = req.UserId,
                AdminId   = CurrentUserId,
                Reason    = req.Reason,
                KickedAt  = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();

            // Force-disconnect kicked user via SignalR
            await _hub.Clients.Group($"user-{req.UserId}").SendAsync("Kicked", new
            {
                Message = "You have been removed by the administrator.",
                Reason  = req.Reason
            });

            return Ok(Success<object>(null!, $"User {user.Name} has been kicked."));
        }

        /// <summary>[Admin] View all kick logs.</summary>
        [HttpGet("users/kick-logs")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(typeof(ApiResponse<List<KickLogResponse>>), 200)]
        public async Task<IActionResult> GetKickLogs()
        {
            var logs = await _db.KickLogs
                .Include(k => k.User)
                .OrderByDescending(k => k.KickedAt)
                .Select(k => new KickLogResponse
                {
                    Id        = k.Id,
                    User      = new UserResponse
                    {
                        Id       = k.User.Id,
                        Name     = k.User.Name,
                        Email    = k.User.Email,
                        Role     = k.User.Role,
                        IsKicked = k.User.IsKicked
                    },
                    Reason    = k.Reason,
                    KickedAt  = k.KickedAt
                })
                .ToListAsync();

            return Ok(Success(logs));
        }

        /// <summary>[Admin] List all users (paged).</summary>
        [HttpGet("users")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(typeof(ApiResponse<PagedResponse<UserResponse>>), 200)]
        public async Task<IActionResult> GetUsers([FromQuery] int page = 1, [FromQuery] int size = 20)
        {
            var total = await _db.Users.CountAsync();
            var items = await _db.Users
                .OrderBy(u => u.Name)
                .Skip((page - 1) * size).Take(size)
                .Select(u => new UserResponse
                {
                    Id         = u.Id,
                    Name       = u.Name,
                    Email      = u.Email,
                    Role       = u.Role,
                    IsKicked   = u.IsKicked,
                    KickReason = u.KickReason
                })
                .ToListAsync();

            return Ok(Success(new PagedResponse<UserResponse>
            {
                Items      = items,
                TotalCount = total,
                Page       = page,
                PageSize   = size
            }));
        }

        // ════════════════════════════════════════════════════════════════════════
        //  LIVE CHAT — REST history (real-time handled in PollHub.cs via SignalR)
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// [User] Get chat history.
        /// pollId = null → global chat. pollId set → poll-scoped chat room.
        /// Real-time messages go through SignalR PollHub.SendMessage().
        /// This endpoint fetches stored history (paged, newest first).
        /// </summary>
        [HttpGet("chat")]
        [ProducesResponseType(typeof(ApiResponse<PagedResponse<ChatMessageResponse>>), 200)]
        public async Task<IActionResult> GetChatHistory(
            [FromQuery] int? pollId = null,
            [FromQuery] int  page   = 1,
            [FromQuery] int  size   = 50)
        {
            var q = _db.ChatMessages.Include(m => m.User).AsQueryable();
            q = pollId.HasValue ? q.Where(m => m.PollId == pollId) : q.Where(m => m.PollId == null);

            var total = await q.CountAsync();
            var items = await q.OrderByDescending(m => m.SentAt)
                               .Skip((page - 1) * size).Take(size)
                               .ToListAsync();

            return Ok(Success(new PagedResponse<ChatMessageResponse>
            {
                Items = items.Select(m => new ChatMessageResponse
                {
                    Id              = m.Id,
                    UserId          = m.UserId,
                    UserName        = m.User?.Name ?? "",
                    PollId          = m.PollId,
                    Message         = m.IsDeletedByAdmin ? "[Message removed by admin]" : m.Message,
                    SentAt          = m.SentAt,
                    IsDeletedByAdmin = m.IsDeletedByAdmin,
                    DeleteReason    = m.DeleteReason
                }).ToList(),
                TotalCount = total,
                Page       = page,
                PageSize   = size
            }));
        }

        /// <summary>
        /// [Admin] Delete a chat message (soft delete with reason).
        /// Broadcasts MessageDeleted event to all connected clients so frontend
        /// can replace message content in real time.
        /// </summary>
        [HttpDelete("chat/{messageId:int}")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(typeof(ApiResponse<object>), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> DeleteChatMessage(
            int messageId,
            [FromQuery] string reason = "Violated community guidelines.")
        {
            var msg = await _db.ChatMessages.FindAsync(messageId);
            if (msg == null) return NotFound(Fail("Message not found."));

            msg.IsDeleted        = true;
            msg.IsDeletedByAdmin = true;
            msg.DeleteReason     = reason;
            await _db.SaveChangesAsync();

            // Real-time: replace message on all clients
            await _hub.Clients.All.SendAsync("MessageDeleted", new
            {
                MessageId = messageId,
                Reason    = reason
            });

            return Ok(Success<object>(null!, "Message removed."));
        }

        // ════════════════════════════════════════════════════════════════════════
        //  NOTIFICATIONS
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>[User] Get my notifications (personal + broadcasts).</summary>
        [HttpGet("notifications")]
        [ProducesResponseType(typeof(ApiResponse<List<NotificationResponse>>), 200)]
        public async Task<IActionResult> GetNotifications()
        {
            var items = await _db.Notifications
                .Where(n => n.UserId == null || n.UserId == CurrentUserId)
                .OrderByDescending(n => n.CreatedAt)
                .Select(n => new NotificationResponse
                {
                    Id        = n.Id,
                    Title     = n.Title,
                    Body      = n.Body,
                    Type      = n.Type.ToString(),
                    PollId    = n.PollId,
                    IsRead    = n.IsRead,
                    CreatedAt = n.CreatedAt
                })
                .ToListAsync();

            return Ok(Success(items));
        }

        /// <summary>[User] Mark single notification as read.</summary>
        [HttpPatch("notifications/{notifId:int}/read")]
        public async Task<IActionResult> MarkRead(int notifId)
        {
            var n = await _db.Notifications.FindAsync(notifId);
            if (n == null) return NotFound(Fail("Notification not found."));
            n.IsRead = true;
            await _db.SaveChangesAsync();
            return Ok(Success<object>(null!, "Marked as read."));
        }

        /// <summary>[User] Mark all notifications as read.</summary>
        [HttpPatch("notifications/read-all")]
        public async Task<IActionResult> MarkAllRead()
        {
            var notifs = await _db.Notifications
                .Where(n => (n.UserId == null || n.UserId == CurrentUserId) && !n.IsRead)
                .ToListAsync();
            notifs.ForEach(n => n.IsRead = true);
            await _db.SaveChangesAsync();
            return Ok(Success<object>(null!, "All notifications marked as read."));
        }

        /// <summary>[Admin] Send targeted or broadcast notification with SignalR push.</summary>
        [HttpPost("notifications")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(typeof(ApiResponse<NotificationResponse>), 201)]
        public async Task<IActionResult> SendNotification([FromBody] CreateNotificationRequest req)
        {
            var notif = new Notification
            {
                UserId    = req.UserId,
                Title     = req.Title,
                Body      = req.Body,
                Type      = req.Type,
                PollId    = req.PollId,
                CreatedAt = DateTime.UtcNow
            };
            _db.Notifications.Add(notif);
            await _db.SaveChangesAsync();

            var resp = new NotificationResponse
            {
                Id        = notif.Id,
                Title     = notif.Title,
                Body      = notif.Body,
                Type      = notif.Type.ToString(),
                PollId    = notif.PollId,
                IsRead    = false,
                CreatedAt = notif.CreatedAt
            };

            // Push via SignalR
            if (req.UserId.HasValue)
                await _hub.Clients.Group($"user-{req.UserId}").SendAsync("Notification", resp);
            else
                await _hub.Clients.All.SendAsync("Notification", resp);

            return CreatedAtAction(nameof(GetNotifications), null, Success(resp));
        }

        // ════════════════════════════════════════════════════════════════════════
        //  PRIVATE HELPERS
        // ════════════════════════════════════════════════════════════════════════

        private async Task<Poll?> LoadFullPoll(int id) =>
            await _db.Polls
                     .Include(p => p.Category)
                     .Include(p => p.TeamOptions).ThenInclude(o => o.Team).ThenInclude(t => t.Goalkeepers)
                     .Include(p => p.TeamOptions).ThenInclude(o => o.Goalkeeper)
                     .Include(p => p.Votes)
                     .FirstOrDefaultAsync(p => p.Id == id);

        private static PollResponse MapPoll(Poll p, int userId)
        {
            var total    = p.Votes.Count;
            var revealed = p.ResultRevealed;

            return new PollResponse
            {
                Id               = p.Id,
                Title            = p.Title,
                Description      = p.Description,
                Category         = MapCategory(p.Category),
                Status           = p.Status,
                ResultRevealed   = p.ResultRevealed,
                ResultRevealedAt = p.ResultRevealedAt,
                CreatedAt        = p.CreatedAt,
                EndsAt           = p.EndsAt,
                TotalVotes       = total,
                HasVoted         = p.Votes.Any(v => v.UserId == userId),
                TeamOptions      = p.TeamOptions.Select(o =>
                {
                    var vc = p.Votes.Count(v =>
                        v.TeamId == o.TeamId &&
                        (o.GoalkeeperId == null || v.GoalkeeperId == o.GoalkeeperId));

                    return new PollTeamOptionResponse
                    {
                        Id             = o.Id,
                        Team           = MapTeam(o.Team),
                        Goalkeeper     = o.Goalkeeper == null ? null : MapGk(o.Goalkeeper, o.Team?.TeamName ?? ""),
                        FanBannerUrl   = o.FanBannerUrl,
                        AnimationTheme = o.AnimationTheme,
                        VoteCount      = revealed ? vc : 0,
                        VotePercent    = revealed && total > 0 ? Math.Round((double)vc / total * 100, 1) : 0
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
                PollId     = p.Id,
                PollTitle  = p.Title,
                Category   = p.Category.Name,
                TotalVotes = mapped.TotalVotes,
                RevealedAt = p.ResultRevealedAt,
                Results    = sorted,
                Winner     = sorted.FirstOrDefault()
            };
        }

        private static TeamResponse MapTeam(FifaTeams t) => new()
        {
            Id                  = t.Id,
            TeamName            = t.TeamName,
            Confederation       = t.Confederation,
            FIFA_Ranking        = t.FIFA_Ranking,
            Captain             = t.Captain,
            TopScorer           = t.TopScorer,
            Coach               = t.Coach,
            Players             = t.Players,
            WorldCupWins        = t.WorldCupWins,
            WorldCupAppearances = t.WorldCupAppearances,
            Goalkeepers         = t.Goalkeepers?.Select(g => MapGk(g, t.TeamName)).ToList() ?? new()
        };

        private static GoalkeeperResponse MapGk(TeamGoalkeeper g, string teamName) => new()
        {
            Id           = g.Id,
            TeamId       = g.TeamId,
            TeamName     = teamName,
            Name         = g.Name,
            PhotoUrl     = g.PhotoUrl,
            JerseyNumber = g.JerseyNumber
        };

        private static PollCategoryResponse MapCategory(PollCategory c) => new()
        {
            Id             = c.Id,
            Name           = c.Name,
            Description    = c.Description,
            GoalkeeperOnly = c.GoalkeeperOnly
        };

        private static ApiResponse<T> Success<T>(T data, string? msg = null) =>
            new() { Success = true, Data = data, Message = msg };

        private static ApiResponse<object> Fail(string error) =>
            new() { Success = false, Errors = new List<string> { error } };

        private static ApiResponse<object> Fail(List<string> errors) =>
            new() { Success = false, Errors = errors };

        private List<string> GetModelErrors() =>
            ModelState.Values
                      .SelectMany(v => v.Errors)
                      .Select(e => e.ErrorMessage)
                      .ToList();
    }
}
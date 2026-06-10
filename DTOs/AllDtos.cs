using PollApi.Models;
using System.ComponentModel.DataAnnotations;

namespace PollApi.DTOs.Request
{
    // ── Auth ────────────────────────────────────────────────────────────────────

    public class LoginRequest
    {
        [Required, EmailAddress] public string Email { get; set; }
        [Required] public string Password { get; set; }
    }

    public class RegisterRequest
    {
        [Required, MaxLength(100)] public string Name { get; set; }
        [Required, EmailAddress] public string Email { get; set; }
        [Required, MinLength(8)] public string Password { get; set; }
    }

    // ── Team ────────────────────────────────────────────────────────────────────

    public class CreateTeamRequest
    {
        [Required, MaxLength(100)] public string TeamName { get; set; }
        [Required, MaxLength(50)]  public string Confederation { get; set; }
        [Range(1, 300)]            public int FIFA_Ranking { get; set; }
        [Required, MaxLength(100)] public string Captain { get; set; }
        [Required, MaxLength(100)] public string TopScorer { get; set; }
        [Required, MaxLength(100)] public string Coach { get; set; }
        public List<string> Players { get; set; } = new();
        [Range(0, 10)]   public int WorldCupWins { get; set; }
        [Range(0, 100)]  public int WorldCupAppearances { get; set; }
    }

    public class UpdateTeamRequest : CreateTeamRequest { }

    // ── Goalkeeper ──────────────────────────────────────────────────────────────

    public class CreateGoalkeeperRequest
    {
        public int TeamId { get; set; }
        [Required, MaxLength(100)] public string Name { get; set; }
        [MaxLength(300)]           public string PhotoUrl { get; set; }
        [Range(1, 99)]             public int JerseyNumber { get; set; }
    }

    // ── Poll Category ───────────────────────────────────────────────────────────

    public class CreatePollCategoryRequest
    {
        [Required, MaxLength(100)] public string Name { get; set; }
        [MaxLength(500)]           public string Description { get; set; }
        public bool GoalkeeperOnly { get; set; }
    }

    // ── Poll ────────────────────────────────────────────────────────────────────

    public class CreatePollRequest
    {
        [Required, MaxLength(200)] public string Title { get; set; }
        [MaxLength(1000)]          public string Description { get; set; }
        public int CategoryId { get; set; }
        public DateTime? EndsAt { get; set; }
        public List<PollTeamOptionRequest> TeamOptions { get; set; } = new();
    }

    public class PollTeamOptionRequest
    {
        public int TeamId { get; set; }
        public int? GoalkeeperId { get; set; }
        [MaxLength(300)] public string? FanBannerUrl { get; set; }
        [MaxLength(50)]  public string? AnimationTheme { get; set; }
    }

    public class UpdatePollRequest
    {
        [MaxLength(200)] public string? Title { get; set; }
        [MaxLength(1000)] public string? Description { get; set; }
        public DateTime? EndsAt { get; set; }
    }

    public class ChangePollStatusRequest
    {
        [Required] public PollStatus Status { get; set; }
    }

    // ── Vote ────────────────────────────────────────────────────────────────────

    public class CastVoteRequest
    {
        public int PollId { get; set; }
        public int TeamId { get; set; }
        public int? GoalkeeperId { get; set; }
    }

    // ── Chat ────────────────────────────────────────────────────────────────────

    public class SendChatRequest
    {
        [Required, MaxLength(2000)] public string Message { get; set; }
        public int? PollId { get; set; }
    }

    // ── Kick ────────────────────────────────────────────────────────────────────

    public class KickUserRequest
    {
        public int UserId { get; set; }
        [Required, MaxLength(500)] public string Reason { get; set; }
    }

    // ── Notification ────────────────────────────────────────────────────────────

    public class CreateNotificationRequest
    {
        public int? UserId { get; set; }                  // null = broadcast
        [Required, MaxLength(200)] public string Title { get; set; }
        [MaxLength(1000)]          public string Body { get; set; }
        public NotificationType Type { get; set; }
        public int? PollId { get; set; }
    }
}

namespace PollApi.DTOs.Response
{
    // ── Auth ────────────────────────────────────────────────────────────────────

    public class AuthResponse
    {
        public string Token { get; set; }
        public string RefreshToken { get; set; }
        public UserResponse User { get; set; }
    }

    // ── User ────────────────────────────────────────────────────────────────────

    public class UserResponse
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Role { get; set; }
        public bool IsKicked { get; set; }
        public string? KickReason { get; set; }
    }

    // ── Team ────────────────────────────────────────────────────────────────────

    public class TeamResponse
    {
        public int Id { get; set; }
        public string TeamName { get; set; }
        public string Confederation { get; set; }
        public int FIFA_Ranking { get; set; }
        public string Captain { get; set; }
        public string TopScorer { get; set; }
        public string Coach { get; set; }
        public List<string> Players { get; set; }
        public int WorldCupWins { get; set; }
        public int WorldCupAppearances { get; set; }
        public List<GoalkeeperResponse> Goalkeepers { get; set; }
    }

    public class GoalkeeperResponse
    {
        public int Id { get; set; }
        public int TeamId { get; set; }
        public string TeamName { get; set; }
        public string Name { get; set; }
        public string PhotoUrl { get; set; }
        public int JerseyNumber { get; set; }
    }

    // ── Poll Category ───────────────────────────────────────────────────────────

    public class PollCategoryResponse
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool GoalkeeperOnly { get; set; }
    }

    // ── Poll ────────────────────────────────────────────────────────────────────

    public class PollResponse
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public PollCategoryResponse Category { get; set; }
        public PollStatus Status { get; set; }
        public bool ResultRevealed { get; set; }
        public DateTime? ResultRevealedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? EndsAt { get; set; }
        public List<PollTeamOptionResponse> TeamOptions { get; set; }
        public int TotalVotes { get; set; }
        public bool HasVoted { get; set; }               // per-user flag
    }

    public class PollTeamOptionResponse
    {
        public int Id { get; set; }
        public TeamResponse Team { get; set; }
        public GoalkeeperResponse? Goalkeeper { get; set; }
        public string? FanBannerUrl { get; set; }
        public string? AnimationTheme { get; set; }
        public int VoteCount { get; set; }               // 0 unless result revealed
        public double VotePercent { get; set; }          // 0 unless result revealed
    }

    // ── Vote ────────────────────────────────────────────────────────────────────

    public class VoteResponse
    {
        public int Id { get; set; }
        public int PollId { get; set; }
        public string PollTitle { get; set; }
        public int TeamId { get; set; }
        public string TeamName { get; set; }
        public int? GoalkeeperId { get; set; }
        public string? GoalkeeperName { get; set; }
        public DateTime VotedAt { get; set; }
    }

    // ── Chat ────────────────────────────────────────────────────────────────────

    public class ChatMessageResponse
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string UserName { get; set; }
        public int? PollId { get; set; }
        public string Message { get; set; }
        public DateTime SentAt { get; set; }
        public bool IsDeletedByAdmin { get; set; }
        public string? DeleteReason { get; set; }
    }

    // ── Notification ────────────────────────────────────────────────────────────

    public class NotificationResponse
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Body { get; set; }
        public string Type { get; set; }
        public int? PollId { get; set; }
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // ── Result ──────────────────────────────────────────────────────────────────

    public class PollResultResponse
    {
        public int PollId { get; set; }
        public string PollTitle { get; set; }
        public string Category { get; set; }
        public int TotalVotes { get; set; }
        public DateTime? RevealedAt { get; set; }
        public List<PollTeamOptionResponse> Results { get; set; } // sorted desc
        public PollTeamOptionResponse Winner { get; set; }
    }

    // ── Kick ────────────────────────────────────────────────────────────────────

    public class KickLogResponse
    {
        public int Id { get; set; }
        public UserResponse User { get; set; }
        public string Reason { get; set; }
        public DateTime KickedAt { get; set; }
    }

    // ── Generic ─────────────────────────────────────────────────────────────────

    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public T? Data { get; set; }
        public List<string>? Errors { get; set; }

        public static ApiResponse<T> Ok(T data, string? msg = null)
            => new() { Success = true, Data = data, Message = msg };

        public static ApiResponse<T> Fail(string error)
            => new() { Success = false, Errors = new List<string> { error } };
    }

    public class PagedResponse<T>
    {
        public List<T> Items { get; set; }
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    }
}
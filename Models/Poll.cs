using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PollApi.Models
{
    public class PollCategory
    {
        public int Id { get; set; }

        [Required, MaxLength(100)]
        public string Name { get; set; }           // "Golden Glove"

        [MaxLength(500)]
        public string Description { get; set; }

        public bool GoalkeeperOnly { get; set; }   // true → vote for GK, not team
        public bool IsDeleted { get; set; }

        public ICollection<Poll> Polls { get; set; }
    }

    public class Poll
    {
        public int Id { get; set; }

        [Required, MaxLength(200)]
        public string Title { get; set; }

        [MaxLength(1000)]
        public string Description { get; set; }

        public int CategoryId { get; set; }
        public PollCategory Category { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? EndsAt { get; set; }

        public PollStatus Status { get; set; } = PollStatus.Draft;

        public bool ResultRevealed { get; set; }   // admin-only toggle
        public DateTime? ResultRevealedAt { get; set; }

        public bool IsDeleted { get; set; }

        public ICollection<PollTeamOption> TeamOptions { get; set; }
        public ICollection<Vote> Votes { get; set; }
    }

    public enum PollStatus
    {
        Draft = 0,
        Active = 1,
        Closed = 2,
        ResultRevealed = 3
    }

    public class PollTeamOption
    {
        public int Id { get; set; }

        public int PollId { get; set; }
        public Poll Poll { get; set; }

        public int TeamId { get; set; }
        public FifaTeams Team { get; set; }

        public int? GoalkeeperId { get; set; }     // null unless GoalkeeperOnly
        public TeamGoalkeeper? Goalkeeper { get; set; }

        // Fan banner image URL uploaded by admin
        public string? FanBannerUrl { get; set; }
        public string? AnimationTheme { get; set; } // e.g. "confetti-blue", "fire-red"

        public bool IsDeleted { get; set; }
    }

    public class TeamGoalkeeper
    {
        public int Id { get; set; }

        public int TeamId { get; set; }
        public FifaTeams Team { get; set; }

        [Required, MaxLength(100)]
        public string Name { get; set; }

        [MaxLength(200)]
        public string PhotoUrl { get; set; }

        public int JerseyNumber { get; set; }
        public bool IsDeleted { get; set; }

        // Nav
        public ICollection<Vote> Votes { get; set; }
    }

    public class Vote
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public User User { get; set; }

        public int PollId { get; set; }
        public Poll Poll { get; set; }

        public int TeamId { get; set; }
        public FifaTeams Team { get; set; }

        public int? GoalkeeperId { get; set; }
        public TeamGoalkeeper? Goalkeeper { get; set; }

        public DateTime VotedAt { get; set; } = DateTime.UtcNow;

    }

    public class ChatMessage
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public User User { get; set; }

        public int? PollId { get; set; }
        public Poll? Poll { get; set; }

        [Required, MaxLength(2000)]
        public string Message { get; set; }

        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        public bool IsDeleted { get; set; }
        public bool IsDeletedByAdmin { get; set; }
        public string? DeleteReason { get; set; }
    }


    public class Notification
    {
        public int Id { get; set; }

        public int? UserId { get; set; }
        public User? User { get; set; }

        [Required, MaxLength(200)]
        public string Title { get; set; }

        [MaxLength(1000)]
        public string Body { get; set; }

        public NotificationType Type { get; set; }

        public int? PollId { get; set; }
        public Poll? Poll { get; set; }

        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsDeleted { get; set; }
    }

    public enum NotificationType
    {
        ResultRevealed = 0,
        PollStarted = 1,
        PollEnding = 2,
        UserKicked = 3,
        System = 4
    }

    public class KickLog
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public User User { get; set; }

        public int AdminId { get; set; }
        public User Admin { get; set; }

        [Required, MaxLength(500)]
        public string Reason { get; set; }

        public DateTime KickedAt { get; set; } = DateTime.UtcNow;
    }
}
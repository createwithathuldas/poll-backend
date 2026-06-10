using PollApi.DTOs.Request;
using PollApi.DTOs.Response;
using PollApi.Models;

namespace PollApi.Services.Interfaces
{
    public interface IAuthService
    {
        Task<AuthResponse> LoginAsync(LoginRequest req);
        Task<AuthResponse> RegisterAsync(RegisterRequest req);
        Task<AuthResponse> RefreshTokenAsync(string refreshToken);
        Task RevokeTokenAsync(string refreshToken);
    }

    public interface IUserService
    {
        Task<PagedResponse<UserResponse>> GetAllAsync(int page, int size);
        Task<UserResponse> GetByIdAsync(int id);
        Task SoftDeleteAsync(int id);
        Task<UserResponse> UpdateAsync(int id, RegisterRequest req);
        Task KickAsync(KickUserRequest req, int adminId);
        Task<List<KickLogResponse>> GetKickLogsAsync();
    }

    public interface ITeamService
    {
        Task<PagedResponse<TeamResponse>> GetAllAsync(int page, int size, string? confederation);
        Task<TeamResponse> GetByIdAsync(int id);
        Task<TeamResponse> CreateAsync(CreateTeamRequest req);
        Task<TeamResponse> UpdateAsync(int id, UpdateTeamRequest req);
        Task SoftDeleteAsync(int id);

        // Goalkeepers
        Task<GoalkeeperResponse> AddGoalkeeperAsync(CreateGoalkeeperRequest req);
        Task SoftDeleteGoalkeeperAsync(int id);
        Task<List<GoalkeeperResponse>> GetGoalkeepersByTeamAsync(int teamId);
    }

    public interface IPollCategoryService
    {
        Task<List<PollCategoryResponse>> GetAllAsync();
        Task<PollCategoryResponse> GetByIdAsync(int id);
        Task<PollCategoryResponse> CreateAsync(CreatePollCategoryRequest req);
        Task<PollCategoryResponse> UpdateAsync(int id, CreatePollCategoryRequest req);
        Task SoftDeleteAsync(int id);
    }

    public interface IPollService
    {
        Task<PagedResponse<PollResponse>> GetAllAsync(int page, int size, PollStatus? status, int? categoryId, int currentUserId);
        Task<PollResponse> GetByIdAsync(int id, int currentUserId);
        Task<PollResponse> CreateAsync(CreatePollRequest req);
        Task<PollResponse> UpdateAsync(int id, UpdatePollRequest req);
        Task ChangeStatusAsync(int id, PollStatus status);
        Task<PollResultResponse> RevealResultAsync(int id);       // admin only; broadcasts
        Task<PollResultResponse> GetResultAsync(int id);          // only if revealed
        Task SoftDeleteAsync(int id);
    }

    public interface IVoteService
    {
        Task<VoteResponse> CastVoteAsync(CastVoteRequest req, int userId);
        Task<bool> HasVotedAsync(int pollId, int userId);
        Task<VoteResponse?> GetMyVoteAsync(int pollId, int userId);
        Task<List<VoteResponse>> GetMyVotesAsync(int userId);
    }

    public interface IChatService
    {
        Task<PagedResponse<ChatMessageResponse>> GetMessagesAsync(int? pollId, int page, int size);
        Task<ChatMessageResponse> SaveMessageAsync(SendChatRequest req, int userId);
        Task DeleteMessageAsync(int messageId, int adminId, string reason);
    }

    public interface INotificationService
    {
        Task<List<NotificationResponse>> GetMyNotificationsAsync(int userId);
        Task MarkReadAsync(int notificationId, int userId);
        Task MarkAllReadAsync(int userId);
        Task<NotificationResponse> CreateAsync(CreateNotificationRequest req);
        Task BroadcastAsync(string title, string body, NotificationType type, int? pollId = null);
    }
}